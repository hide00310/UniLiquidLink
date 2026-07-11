"""Core JSON-RPC 2.0 peer over a length-prefixed byte stream (anyio-based).

Shared by the client transports and the server middleware. Framing is a 4-byte
big-endian length prefix followed by a UTF-8 JSON body, matching the C# wire
protocol. This module owns request/response correlation and the receive loop;
each transport only has to supply a byte stream.
"""
from __future__ import annotations
import json
import struct
import sys
from typing import Any

import anyio
from anyio.streams.buffered import BufferedByteReceiveStream

import logging
logger = logging.getLogger(__name__)

_LEN = struct.Struct(">I")

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


def encode_frame(msg: dict, default=None) -> bytes:
    """Encode a message as a 4-byte big-endian length prefix + JSON body.

    ``default`` is forwarded to ``json.dumps`` to serialize non-native values.
    """
    body = json.dumps(msg, default=default).encode("utf-8")
    return _LEN.pack(len(body)) + body


def decode_frame(raw) -> dict:
    """Decode a single length-prefixed frame (where one message == one frame)."""
    if isinstance(raw, str):
        raw = raw.encode("utf-8")
    length = _LEN.unpack(raw[:4])[0]
    return json.loads(raw[4:4 + length])


class _Slot:
    """Pending-request slot: an event plus the resolved result or error."""
    __slots__ = ("event", "result", "error")

    def __init__(self):
        self.event = anyio.Event()
        self.result = None
        self.error = None


class JsonRpcPeer:
    """JSON-RPC 2.0 over a bidirectional byte stream with 4-byte length framing.

    The stream must provide async ``send(bytes)``, ``receive(max_bytes)`` and
    ``aclose()``.
    """

    def __init__(self, stream, default=None, object_hook=None):
        self._stream = stream
        self._reader = BufferedByteReceiveStream(stream)
        self._default = default
        self._object_hook = object_hook
        self._next_id = 1
        self._pending = {}
        self._closed = False

    @property
    def closed(self) -> bool:
        return self._closed

    async def request(self, method: str, params: list) -> Any:
        """Send a request and await its response."""
        if self._closed:
            raise ConnectionClosedError("Not connected")
        call_id = self._next_id
        self._next_id += 1
        slot = _Slot()
        self._pending[call_id] = slot
        logger.debug("request %s %s", method, params)
        await self._stream.send(encode_frame(
            {"jsonrpc": "2.0", "id": call_id, "method": method, "params": params},
            self._default))
        await slot.event.wait()
        if slot.error is not None:
            raise slot.error
        return slot.result

    async def notify(self, method: str, params: list) -> None:
        """Send a fire-and-forget notification (no id)."""
        if self._closed:
            raise ConnectionClosedError("Not connected")
        logger.debug("notify %s %s", method, params)
        await self._stream.send(encode_frame(
            {"jsonrpc": "2.0", "method": method, "params": params}, self._default))

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
                self._dispatch(json.loads(body, object_hook=self._object_hook))
        finally:
            self._reject_all(ConnectionClosedError("Connection closed"))

    def _dispatch(self, msg: dict) -> None:
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
        slot.event.set()

    def _reject_all(self, error: Exception) -> None:
        self._closed = True
        logger.debug("JsonRpcPeer._reject_all %s", error)
        for slot in self._pending.values():
            if not slot.event.is_set():
                slot.error = error
                slot.event.set()
        self._pending.clear()

    async def aclose(self) -> None:
        self._closed = True
        logger.debug("JsonRpcPeer.aclose")
        await self._stream.aclose()


class StdioByteStream:
    """Byte stream over blocking stdin/stdout, read in a worker thread.

    Windows pipes do not support asyncio's connect_read_pipe, so reads use a
    blocking read offloaded to a thread (abandoned on cancel so close never hangs).
    """

    def __init__(self, reader=None, writer=None):
        self._in = reader if reader is not None else sys.stdin.buffer
        self._out = writer if writer is not None else sys.stdout.buffer

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
