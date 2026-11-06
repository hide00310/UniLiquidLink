"""Core JSON-RPC 2.0 peer over a length-prefixed byte stream (anyio-based).

Shared by the client transports and the server middleware. Framing is a 4-byte
little-endian length prefix followed by a UTF-8 JSON body, matching the C# wire
protocol. This module owns request/response correlation and the receive loop;
each transport only has to supply a byte stream.
"""
from __future__ import annotations
import json
import struct
import sys
import time
from typing import Any, BinaryIO, Callable, Dict, List, Optional, Union

import anyio
from anyio.streams.buffered import BufferedByteReceiveStream

from ._interfaces import ByteStream
from ._timing import RpcTimingStats

import logging
logger = logging.getLogger(__name__)

_LEN = struct.Struct("<I")

# Stream-end conditions that terminate the receive loop.
_STREAM_END = (
    anyio.EndOfStream,
    anyio.IncompleteRead,
    anyio.ClosedResourceError,
    anyio.BrokenResourceError,
)


class ConnectionClosedError(Exception):
    """Raised when an RPC is attempted on a closed connection."""


class RpcError(Exception):
    """Raised when the server returns a JSON-RPC error response."""


def encode_frame(msg: Dict[str, Any], default: Optional[Callable[[Any], Any]] = None) -> bytes:
    """Encode a message as a 4-byte little-endian length prefix + JSON body.

    ``default`` is forwarded to ``json.dumps`` to serialize non-native values.
    """
    body = json.dumps(msg, default=default).encode("utf-8")
    return _LEN.pack(len(body)) + body


def decode_frame(raw: Union[bytes, str]) -> Dict[str, Any]:
    """Decode a single length-prefixed frame (where one message == one frame)."""
    if isinstance(raw, str):
        raw = raw.encode("utf-8")
    length = _LEN.unpack(raw[:4])[0]
    return json.loads(raw[4:4 + length])


class _Slot:
    """Pending-request slot: an event plus the resolved result or error."""
    __slots__ = ("event", "result", "error", "sent_at",
                 "response_network_time", "request_network_time",
                 "remote_time", "decode_time")

    def __init__(self) -> None:
        self.event: anyio.Event = anyio.Event()
        self.result = None
        self.error: Optional[Exception] = None
        # This request's own `sent_at` (the local send timestamp), stashed
        # here so `_dispatch` can pair it with the response's echoed
        # `received_at` to derive `request_network_time`.
        self.sent_at: Optional[float] = None
        # Wall-clock network transit times, set in `_dispatch` from the
        # response's echoed `sent_at`/`received_at`; stay 0.0 when the peer
        # does not echo them back (i.e. the original request had no `sent_at`).
        self.response_network_time: float = 0.0
        self.request_network_time: float = 0.0
        # Time the peer spent between receiving the request and sending the
        # response, derived in `_dispatch` from the response's own
        # `sent_at`/`received_at` pair (same-process clock, no skew).
        self.remote_time: float = 0.0
        # Time spent decoding the response frame in `serve()`; folded into
        # `local_processing_time` by `request()`.
        self.decode_time: float = 0.0


class JsonRpcPeer:
    """JSON-RPC 2.0 over a bidirectional byte stream with 4-byte length framing.

    The stream must provide async ``send(bytes)``, ``receive(max_bytes)`` and
    ``aclose()``.
    """

    def __init__(
        self,
        stream: ByteStream,
        default: Optional[Callable[[Any], Any]] = None,
        object_hook: Optional[Callable[[Dict[str, Any]], Any]] = None,
        timing: Optional[RpcTimingStats] = None,
    ):
        self._stream: ByteStream = stream
        self._reader: BufferedByteReceiveStream = BufferedByteReceiveStream(stream)
        self._default: Optional[Callable[[Any], Any]] = default
        self._object_hook: Optional[Callable[[Dict[str, Any]], Any]] = object_hook
        self._next_id: int = 1
        self._pending: Dict[int, _Slot] = {}
        self._closed: bool = False
        # Per-connection RPC timing; disabled by default unless the caller opts in.
        self.timing: RpcTimingStats = timing if timing is not None else RpcTimingStats()

    @property
    def closed(self) -> bool:
        return self._closed

    async def request(self, method: str, params: List[Any]):
        """Send a request and await its response.

        When `self.timing` is enabled, records this RPC's local processing
        time (frame building + response decoding), remote time (the peer's
        own processing), and wall-clock network transit times (from which
        `communication_time` is derived), plus wire byte counts. A
        `"sent_at"` timestamp is embedded in the outgoing message so the
        receiver can measure its own request-side transit time and echo back
        `"received_at"`/`"sent_at"` on the response; this only happens when
        timing is enabled, so the payload is unchanged when the feature is
        off.
        """
        if self._closed:
            raise ConnectionClosedError("Not connected")
        call_id = self._next_id
        self._next_id += 1
        slot = _Slot()
        self._pending[call_id] = slot
        logger.debug("request %s %s", method, params)
        msg = {"jsonrpc": "2.0", "id": call_id, "method": method, "params": params}
        if self.timing.enabled:
            local_start = time.perf_counter()
            msg["sent_at"] = time.time()      # closes the local window here
            slot.sent_at = msg["sent_at"]
            local_elapsed = time.perf_counter() - local_start
            frame = encode_frame(msg, self._default)   # encoding belongs to request_network_time
            self.timing.record_bytes(sent=len(frame))
            await self._stream.send(frame)
            await slot.event.wait()
            self.timing.record(
                local_processing_time=local_elapsed + slot.decode_time,
                remote_time=slot.remote_time,
                response_network_time=slot.response_network_time,
                request_network_time=slot.request_network_time)
        else:
            await self._stream.send(encode_frame(msg, self._default))
            await slot.event.wait()
        if slot.error is not None:
            raise Exception(slot.error, f'{method}({params})')
        return slot.result

    async def notify(self, method: str, params: List[Any]) -> None:
        """Send a fire-and-forget notification (no id)."""
        if self._closed:
            raise ConnectionClosedError("Not connected")
        logger.debug("notify %s %s", method, params)
        frame = encode_frame(
            {"jsonrpc": "2.0", "method": method, "params": params}, self._default)
        self.timing.record_bytes(sent=len(frame))
        await self._stream.send(frame)

    async def serve(self) -> None:
        """Receive loop: resolve pending requests; ignore inbound notifications."""
        try:
            while True:
                try:
                    header = await self._reader.receive_exactly(4)
                    length = _LEN.unpack(header)[0]
                    body = await self._reader.receive_exactly(length)
                except _STREAM_END:
                    break
                received_at = time.time()
                self.timing.record_bytes(received=4 + length)
                decode_start = time.perf_counter()
                msg = json.loads(body, object_hook=self._object_hook)
                decode_time = time.perf_counter() - decode_start
                self._dispatch(msg, received_at, decode_time)
        finally:
            self._reject_all(ConnectionClosedError("Connection closed"))

    def _dispatch(
        self,
        msg: Dict[str, Any],
        received_at: Optional[float] = None,
        decode_time: float = 0.0,
    ) -> None:
        msg_id = msg.get("id")
        if msg_id is None:
            return  # notification / server push: ignored (parity with prior behavior)
        slot = self._pending.pop(msg_id, None)
        if slot is None or slot.event.is_set():
            return
        error = msg.get("error")
        if error is not None:
            message = error.get("message", "RPC error") if isinstance(error, dict) else str(error)
            logger.error("received error response: %s", message)
            slot.error = RpcError(message)
        else:
            slot.result = msg.get("result")
        slot.decode_time = decode_time
        # Correlate the embedded timestamps with THIS specific slot (found via the
        # msg_id lookup above), not a peer-level shared variable: with multiple requests
        # in flight, serve() can process another frame before request() next runs, so a
        # shared "last received_at" would risk being overwritten before it's read.
        resp_sent_at = msg.get("sent_at")
        resp_received_at = msg.get("received_at")
        if resp_sent_at is not None and received_at is not None:
            slot.response_network_time = received_at - resp_sent_at
        if resp_received_at is not None and slot.sent_at is not None:
            slot.request_network_time = resp_received_at - slot.sent_at
        if resp_sent_at is not None and resp_received_at is not None:
            slot.remote_time = resp_sent_at - resp_received_at
        slot.event.set()

    def _reject_all(self, error: Exception) -> None:
        logger.debug("JsonRpcPeer._reject_all %s", error)
        self._closed = True
        for slot in self._pending.values():
            if not slot.event.is_set():
                slot.error = error
                slot.event.set()
        self._pending.clear()

    async def aclose(self) -> None:
        logger.debug("JsonRpcPeer.aclose")
        self._closed = True
        await self._stream.aclose()


class StdioByteStream(ByteStream):
    """Byte stream over blocking stdin/stdout, read in a worker thread.

    Windows pipes do not support asyncio's connect_read_pipe, so reads use a
    blocking read offloaded to a thread (abandoned on cancel so close never hangs).
    """

    def __init__(self, reader: Optional[BinaryIO] = None, writer: Optional[BinaryIO] = None):
        self._in: BinaryIO = reader if reader is not None else sys.stdin.buffer
        self._out: BinaryIO = writer if writer is not None else sys.stdout.buffer

    async def receive(self, max_bytes: int = 65536) -> bytes:
        data = await anyio.to_thread.run_sync(
            self._in.read1, max_bytes, abandon_on_cancel=True)
        if not data:
            raise anyio.EndOfStream
        return data

    async def send(self, data: bytes) -> None:
        def _write():
            self._out.write(data)
            self._out.flush()
        await anyio.to_thread.run_sync(_write)

    async def aclose(self) -> None:
        pass
