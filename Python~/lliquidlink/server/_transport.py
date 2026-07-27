"""Server-side transport implementations: accept external client connections.

Unlike the client-side StreamTransport (one JsonRpcPeer per instance), a
ServerTransport accepts many concurrent connections and dispatches each one to
a handler. TcpServerTransport is the default, built on anyio's TCP listener;
subclass ServerTransport to plug in Unix sockets, etc.
"""
from __future__ import annotations

import abc
import struct
from typing import Awaitable, Callable, Optional

import anyio
import anyio.abc
from anyio.streams.buffered import BufferedByteReceiveStream

from ._interfaces import MessageStream

import logging
logger = logging.getLogger(__name__)

_LEN = struct.Struct("<I")

# Stream-end conditions that terminate a connection's receive loop.
_STREAM_END = (
    anyio.EndOfStream,
    anyio.IncompleteRead,
    anyio.ClosedResourceError,
    anyio.BrokenResourceError,
)


class ServerTransport(abc.ABC):
    """Base transport: accepts external client connections and dispatches each to a handler."""

    @abc.abstractmethod
    async def serve(self, handler: Callable[[MessageStream], Awaitable[None]]) -> None:
        """Start accepting connections, invoking handler(connection) for each. Runs until stop()."""
        raise NotImplementedError

    @abc.abstractmethod
    async def stop(self) -> None:
        """Signal serve() to return."""
        raise NotImplementedError


class _TcpConnection(MessageStream):
    """Adapt an anyio SocketStream to the message-iterator interface `_handle_client` expects.

    Raw TCP has no message boundaries, so each iteration reads one 4-byte
    length prefix + body (the same shape `encode_frame` produces) and yields
    it whole for `decode_frame` to parse.
    """

    def __init__(self, stream: anyio.abc.SocketStream):
        self._stream = stream
        self._reader: BufferedByteReceiveStream = BufferedByteReceiveStream(stream)

    def __aiter__(self) -> _TcpConnection:
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
        self._host: str = host
        self._port: int = port
        self._stop_event: Optional[anyio.Event] = None

    async def serve(self, handler: Callable[[MessageStream], Awaitable[None]]) -> None:
        self._stop_event = anyio.Event()
        logger.info("serve %s:%d", self._host, self._port)

        async def _on_connect(stream: anyio.abc.SocketStream) -> None:
            connection = _TcpConnection(stream)
            try:
                await handler(connection)
            finally:
                await connection.aclose()

        listener = await anyio.create_tcp_listener(local_host=self._host, local_port=self._port)
        async with listener:
            async with anyio.create_task_group() as tg:
                tg.start_soon(listener.serve, _on_connect)
                await self._stop_event.wait()
                tg.cancel_scope.cancel()

    async def stop(self) -> None:
        if self._stop_event is not None:
            self._stop_event.set()
