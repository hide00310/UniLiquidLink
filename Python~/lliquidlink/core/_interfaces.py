"""Structural (Protocol) types for the byte-stream shape shared by core and its transports."""
from __future__ import annotations
from typing import Protocol


class ByteStream(Protocol):
    """Bidirectional byte stream: anything with async send/receive/aclose.

    Satisfied structurally by StdioByteStream, anyio.abc.SocketStream, and test doubles.
    """

    async def send(self, data: bytes) -> None:
        ...

    async def receive(self, max_bytes: int = ...) -> bytes:
        ...

    async def aclose(self) -> None:
        ...
