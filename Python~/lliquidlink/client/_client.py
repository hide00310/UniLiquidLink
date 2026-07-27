"""Client: anyio runtime and RPC dispatch."""
from __future__ import annotations
from typing import Any, Callable, Dict, List, Optional, TYPE_CHECKING
if TYPE_CHECKING:
    from ._interfaces import Transport

import anyio

from ._event import Event
from ._proxy import ObjectProxy, PropertyProxy
from ._release import ReleaseManager
from ._serialization import Serialization

import logging
logger = logging.getLogger(__name__)

class Client:
    """Client that connects to a Unity Editor server and sends RPC commands.

    Register a handler on the :attr:`on_execute` event with ``+=``, then call
    :meth:`mainloop`.
    """

    def __init__(self, transport: Transport, verify_releases: bool = False):
        self._transport: Transport = transport
        self._serialization: Serialization = Serialization(lambda data: ObjectProxy(data, self._transport, self._release.track, self._make_property_proxy))
        transport.bind_codec(self._serialization)
        self._release: ReleaseManager = ReleaseManager(transport, verify=verify_releases)
        self.on_execute: Event = Event()

    # ── RPC dispatch ─────────────────────────────────────────────────────────

    def __getattr__(self, name: str) -> PropertyProxy:
        """Treat any undefined non-underscore attribute as a Unity RPC method."""
        if name.startswith("_"):
            raise AttributeError(name)
        return self._make_property_proxy(None, [name])

    def _make_property_proxy(self, obj: Optional[Dict[str, Any]], chain: List[str]) -> PropertyProxy:
        return PropertyProxy(obj, chain, self._transport, self._make_property_proxy)

    def flush_releases(self) -> Optional[List[int]]:
        """Send pending object releases now (callable from a worker thread)."""
        return self._release.flush()

    # ── Lifecycle ────────────────────────────────────────────────────────────

    @property
    def is_running(self) -> bool:
        return not self._transport.closed

    def mainloop(self) -> None:
        """Connect, run on_execute in a worker thread, flush, then disconnect."""
        anyio.run(self._amain)

    async def _amain(self) -> None:
        await self._transport.open()
        try:
            await self.execute(self.on_execute)
            await self._release.flush_async()
        finally:
            await self._transport.aclose()

    async def connect(self) -> None:
        """Open the connection to the Unity server."""
        await self._transport.open()

    async def disconnect(self) -> None:
        """Close the connection to the Unity server."""
        await self._transport.aclose()

    async def execute(self, on_execute: Callable[[Client], None]) -> None:
        """Run a callback in a worker thread with this client as its argument."""
        await anyio.to_thread.run_sync(on_execute, self)

    def add_abbreviated_classes(self, class_names: List[str]) -> None:
        """Register a class name whose methods can be called without namespace prefix."""
        if isinstance(class_names, str):
            class_names = [class_names]
        self._transport.call_sync("add_abbreviated_classes", [class_names])

    def add_abbreviated_namespaces(self, namespaces: List[str]) -> None:
        """Register namespaces whose types can be referred to by simple name."""
        if isinstance(namespaces, str):
            namespaces = [namespaces]
        self._transport.call_sync("add_abbreviated_namespaces", [namespaces])
