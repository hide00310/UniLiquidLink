"""Structural (Protocol) types for the shapes duck-typed across the server package."""
from __future__ import annotations
from typing import Any, List, Protocol


class MessageStream(Protocol):
    """Async-iterable of raw frames plus send, used by `_handle_client`.

    Satisfied structurally by `_TcpConnection` and test doubles (e.g. FakeWebSocket).
    """

    def __aiter__(self) -> "MessageStream":
        ...

    async def __anext__(self) -> bytes:
        ...

    async def send(self, data: bytes) -> None:
        ...


class RpcBridge(Protocol):
    """The `_handle_client` side of the C# IPC bridge: call/anotify only.

    Satisfied structurally by IpcBridge and test doubles (e.g. FakeBridge).
    """

    async def call(self, method: str, params: List[Any]):
        ...

    async def anotify(self, method: str, params: List[Any]) -> None:
        ...
