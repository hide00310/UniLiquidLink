"""Forwards JSON-RPC calls from Server to C# Unity over stdin/stdout.

Reuses the shared JsonRpcPeer core (4-byte framing + correlation) so the wire
handling lives in exactly one place.
"""
import asyncio
from typing import Any

from ..core import JsonRpcPeer, StdioByteStream
import logging
logger = logging.getLogger(__name__)

class IpcBridge:
    """Talks to the C# Unity process over stdio using the shared JSON-RPC core."""

    def __init__(self, stream=None):
        # stream is injectable for tests; defaults to this process's stdio.
        self._peer = JsonRpcPeer(stream if stream is not None else StdioByteStream())
        self._task = None

    async def connect(self) -> None:
        """Start the background loop that reads C# responses from stdin."""
        logger.info("connect %s", self._peer._stream)
        self._task = asyncio.ensure_future(self._peer.serve())

    async def call(self, method: str, params: list) -> Any:
        """Send a JSON-RPC request to C# and await the response."""
        return await self._peer.request(method, params)

    def notify(self, method: str, params: list) -> None:
        """Send a JSON-RPC notification to C# (fire-and-forget, no id)."""
        asyncio.ensure_future(self._peer.notify(method, params))

    async def anotify(self, method: str, params: list) -> None:
        """Send a JSON-RPC notification to C# and await delivery."""
        await self._peer.notify(method, params)
