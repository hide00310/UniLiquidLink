"""Server-side transport implementations: accept external client connections.

Unlike the client-side StreamTransport (one JsonRpcPeer per instance), a
ServerTransport accepts many concurrent connections and dispatches each one to
a handler. TcpServerTransport is the default, built on anyio's TCP listener;
subclass ServerTransport to plug in Unix sockets, etc.
"""
from __future__ import annotations

import struct

import anyio
from anyio.streams.buffered import BufferedByteReceiveStream

import logging
logger = logging.getLogger(__name__)

_LEN = struct.Struct(">I")

# Stream-end conditions that terminate a connection's receive loop.
_STREAM_END = (
    anyio.EndOfStream,
    anyio.IncompleteRead,
    anyio.ClosedResourceError,
    anyio.BrokenResourceError,
)


class ServerTransport:
    """Base transport: accepts external client connections and dispatches each to a handler."""

    async def serve(self, handler) -> None:
        """Start accepting connections, invoking handler(connection) for each. Runs until stop()."""
        raise NotImplementedError

    async def stop(self) -> None:
        """Signal serve() to return."""
        raise NotImplementedError


class _TcpConnection:
    """Adapt an anyio SocketStream to the message-iterator interface `_handle_client` expects.

    Raw TCP has no message boundaries, so each iteration reads one 4-byte
    length prefix + body (the same shape `encode_frame` produces) and yields
    it whole for `decode_frame` to parse.
    """

    def __init__(self, stream):
        self._stream = stream
        self._reader = BufferedByteReceiveStream(stream)

    def __aiter__(self):
        return self

    async def __anext__(self) -> bytes:
        try:
            header = await self._reader.receive_exactly(4)
            body = await self._reader.receive_exactly(_LEN.unpack(header)[0])
        except _STREAM_END:
            raise StopAsyncIteration
        return header + body

    async def send(self, data: bytes) -> None:
        await self._stream.send(data)

    async def aclose(self) -> None:
        await self._stream.aclose()


class TcpServerTransport(ServerTransport):
    """Default ServerTransport: a raw TCP listener, one connection per client."""

    def __init__(self, host: str = "localhost", port: int = 8700):
        self._host = host
        self._port = port
        self._stop_event = None

    async def serve(self, handler) -> None:
        self._stop_event = anyio.Event()
        logger.info("serve %s:%d", self._host, self._port)

        async def _on_connect(stream):
            await handler(_TcpConnection(stream))

        listener = await anyio.create_tcp_listener(local_host=self._host, local_port=self._port)
        async with listener:
            async with anyio.create_task_group() as tg:
                tg.start_soon(listener.serve, _on_connect)
                await self._stop_event.wait()
                tg.cancel_scope.cancel()

    async def stop(self) -> None:
        if self._stop_event is not None:
            self._stop_event.set()
