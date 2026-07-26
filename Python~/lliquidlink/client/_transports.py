"""Transport implementations: byte-stream openers wrapping the JSON-RPC core.

Currently TCP and stdio. Unix-domain-socket can be added later by writing
an ``_open`` function that uses ``anyio.connect_unix`` and passing it to
``StreamTransport``.
"""
from __future__ import annotations

import asyncio

import anyio
import anyio.abc

from ..core import ByteStream, JsonRpcPeer, StdioByteStream
from ._interfaces import Transport
from typing import Any, Awaitable, Callable, Coroutine, Dict, List, Optional, TYPE_CHECKING
if TYPE_CHECKING:
    from ._serialization import Serialization

import logging
logger = logging.getLogger(__name__)

async def _await(coro: Coroutine[Any, Any, Any]):
    """Await a coroutine; used to run it on the event loop from a worker thread."""
    return await coro


class StreamTransport(Transport):
    """Base transport: opens a byte stream and runs a JsonRpcPeer over it.

    The serve loop runs as a plain asyncio Task so open/aclose can be called
    from different asyncio tasks without triggering anyio cancel-scope affinity
    errors.
    """

    def __init__(self, open: Callable[[], Awaitable[ByteStream]]):
        # Async callable that opens and returns the underlying byte stream.
        self._open: Callable[[], Awaitable[ByteStream]] = open
        self._peer: Optional[JsonRpcPeer] = None
        self._serve_task: Optional["asyncio.Task[None]"] = None
        self._default: Optional[Callable[[Any], Any]] = None
        self._object_hook: Optional[Callable[[Dict[str, Any]], Any]] = None

    def bind_codec(self, serialization : Serialization) -> None:
        """Set the json.dumps default / json.loads object_hook used by the peer."""
        self._default = serialization.encode_default
        self._object_hook = serialization.object_hook

    async def open(self) -> None:
        stream = await self._open()
        self._peer = JsonRpcPeer(stream, self._default, self._object_hook)
        self._serve_task = asyncio.ensure_future(self._peer.serve())

    async def aclose(self) -> None:
        if self._peer is not None:
            await self._peer.aclose()
            self._peer = None
        if self._serve_task is not None:
            task = self._serve_task
            self._serve_task = None
            if not task.done():
                task.cancel()
            try:
                await task
            except (asyncio.CancelledError, Exception):
                pass

    @property
    def closed(self) -> bool:
        return self._peer is None or self._peer.closed

    async def rpc_call(self, method: str, params: List[Any]):
        logger.debug("rpc_call %s(%s)", method, params)
        ret = await self._peer.request(method, params)
        logger.debug("rpc_call ret: %s", ret)
        return ret

    async def rpc_notify(self, method: str, params: List[Any]) -> None:
        await self._peer.notify(method, params)

    def call_sync(self, method: str, params: List[Any]):
        return self.run(self.rpc_call(method, params))

    def run(self, coro: Coroutine[Any, Any, Any]):
        """Run a coroutine synchronously from a worker thread, else return it."""
        try:
            return anyio.from_thread.run(_await, coro)
        except RuntimeError:
            return coro


def StdioJsonRpcTransport() -> StreamTransport:
    """JSON-RPC transport over this process's stdin/stdout."""
    async def _open() -> StdioByteStream:
        return StdioByteStream()
    return StreamTransport(_open)


def TcpJsonRpcTransport(host: str, port: int) -> StreamTransport:
    """JSON-RPC transport over a raw TCP socket.

    anyio's SocketStream already implements the send/receive/aclose
    byte-stream interface JsonRpcPeer expects, so no adapter is needed.
    """
    async def _open() -> anyio.abc.SocketStream:
        return await anyio.connect_tcp(host, port)
    return StreamTransport(_open)
