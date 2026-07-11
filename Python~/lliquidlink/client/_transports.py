"""Transport implementations: byte-stream openers wrapping the JSON-RPC core.

Currently TCP and stdio. Unix-domain-socket can be added later by
subclassing ``StreamTransport`` and implementing ``_open()`` with
``anyio.connect_unix``.
"""
from __future__ import annotations

import asyncio

import anyio

from ..core import JsonRpcPeer, StdioByteStream
from typing import Any, TYPE_CHECKING
if TYPE_CHECKING:
    from ._serialization import Serialization

import logging
logger = logging.getLogger(__name__)

class StreamTransport:
    """Base transport: opens a byte stream and runs a JsonRpcPeer over it.

    The serve loop runs as a plain asyncio Task so open/aclose can be called
    from different asyncio tasks without triggering anyio cancel-scope affinity
    errors.
    """

    def __init__(self):
        self._peer = None
        self._serve_task = None
        self._default = None
        self._object_hook = None

    def bind_codec(self, serialization : Serialization) -> None:
        """Set the json.dumps default / json.loads object_hook used by the peer."""
        self._default = serialization.encode_default
        self._object_hook = serialization.object_hook

    async def _open(self):
        """Open and return the underlying byte stream. Override in subclasses."""
        raise NotImplementedError

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

    async def rpc_call(self, method, params):
        logger.debug("rpc_call %s(%s)", method, params)
        ret = await self._peer.request(method, params)
        logger.debug("rpc_call ret: %s", ret)
        return ret

    async def rpc_notify(self, method, params):
        await self._peer.notify(method, params)


class StdioJsonRpcTransport(StreamTransport):
    """JSON-RPC transport over this process's stdin/stdout."""

    async def _open(self):
        return StdioByteStream()


class TcpJsonRpcTransport(StreamTransport):
    """JSON-RPC transport over a raw TCP socket.

    anyio's SocketStream already implements the send/receive/aclose
    byte-stream interface JsonRpcPeer expects, so no adapter is needed.
    """

    def __init__(self, host: str, port: int):
        super().__init__()
        self._host = host
        self._port = port

    async def _open(self):
        return await anyio.connect_tcp(self._host, self._port)
