"""Structural (Protocol) types for the shapes duck-typed across the client package."""
from __future__ import annotations
from typing import Any, Coroutine, List, Protocol, TYPE_CHECKING
if TYPE_CHECKING:
    from ._serialization import Serialization


class Transport(Protocol):
    """RPC transport shape used by Client/ObjectProxy/PropertyProxy/ReleaseManager.

    Satisfied structurally by StreamTransport; custom transport implementations
    only need to match this shape, not inherit from it.
    """

    def bind_codec(self, serialization: Serialization) -> None:
        ...

    async def open(self) -> None:
        ...

    async def aclose(self) -> None:
        ...

    @property
    def closed(self) -> bool:
        ...

    async def rpc_call(self, method: str, params: List[Any]):
        ...

    async def rpc_notify(self, method: str, params: List[Any]) -> None:
        ...

    def call_sync(self, method: str, params: List[Any]):
        ...

    def run(self, coro: Coroutine[Any, Any, Any]):
        ...


class SupportsAsDict(Protocol):
    """Anything exposing `_asdict()` for wire encoding (e.g. ObjectProxy)."""

    def _asdict(self) -> dict:
        ...
