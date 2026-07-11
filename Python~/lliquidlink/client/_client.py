"""Client: anyio runtime, RPC dispatch, object-release batching."""
from __future__ import annotations
import functools
import json
import threading
import weakref
from typing import Any, List, TYPE_CHECKING
if TYPE_CHECKING:
    from ._transports import StreamTransport

import anyio

from ._event import Event
from ._proxy import ObjectProxy, PropertyProxy
from ._serialization import Serialization

import logging
logger = logging.getLogger(__name__)

async def _await(coro):
    """Await a coroutine; used to run it on the event loop from a worker thread."""
    return await coro


def gc_flush(func):
    """Decorator: flush pending Unity object releases after the method returns."""
    @functools.wraps(func)
    def wrapper(self, *args, **kwargs):
        try:
            return func(self, *args, **kwargs)
        finally:
            self.flush_releases()
    return wrapper


class Client:
    """Client that connects to a Unity Editor server and sends RPC commands.

    Register a handler on the :attr:`on_execute` event with ``+=``, then call
    :meth:`mainloop`.
    """

    def __init__(self, transport : StreamTransport):
        self._transport = transport
        self._serialization = Serialization(lambda data: ObjectProxy(self, data))
        transport.bind_codec(self._serialization)
        self._pending_releases = set()
        self._release_lock = threading.Lock()
        self.on_execute = Event()

    # ── RPC dispatch ─────────────────────────────────────────────────────────

    def __getattr__(self, name: str) -> PropertyProxy:
        """Treat any undefined non-underscore attribute as a Unity RPC method."""
        if name.startswith("_"):
            raise AttributeError(name)
        return PropertyProxy(self, None, [name])

    async def _call_async(self, method: str, params: list) -> Any:
        return await self._transport.rpc_call(method, params)

    def _call_sync(self, method: str, params: list) -> Any:
        return self._run(self._call_async(method, params))

    def _run(self, coro):
        """Run a coroutine synchronously from a worker thread, else return it."""
        try:
            return anyio.from_thread.run(_await, coro)
        except RuntimeError:
            return coro

    # ── Object release batching ──────────────────────────────────────────────

    def _track_release(self, proxy, data: dict) -> None:
        instance_id = data.get("instanceId")
        if instance_id is None:
            return
        weakref.finalize(proxy, self._schedule_release, instance_id)

    def _schedule_release(self, instance_id: int) -> None:
        with self._release_lock:
            self._pending_releases.add(instance_id)

    async def _flush_releases(self) -> None:
        with self._release_lock:
            if not self._pending_releases:
                return
            ids = sorted(self._pending_releases)
            self._pending_releases.clear()
        if self._transport.closed:
            return
        # await self._transport.rpc_notify("release_objects", [json.dumps(ids)])

    def flush_releases(self) -> None:
        """Send pending object releases now (callable from a worker thread)."""
        self._run(self._flush_releases())

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
            await self._flush_releases()
        finally:
            await self._transport.aclose()

    async def connect(self) -> None:
        """Open the connection to the Unity server."""
        await self._transport.open()

    async def disconnect(self) -> None:
        """Close the connection to the Unity server."""
        await self._transport.aclose()

    async def execute(self, on_execute) -> None:
        """Run a callback in a worker thread with this client as its argument."""
        await anyio.to_thread.run_sync(on_execute, self)

    def add_abbreviated_classes(self, class_names: List[str]) -> None:
        """Register a class name whose methods can be called without namespace prefix."""
        if isinstance(class_names, str):
            class_names = [class_names]
        self._call_sync("add_abbreviated_classes", [class_names])

    def add_abbreviated_namespaces(self, namespaces: List[str]) -> None:
        """Register namespaces whose types can be referred to by simple name."""
        if isinstance(namespaces, str):
            namespaces = [namespaces]
        self._call_sync("add_abbreviated_namespaces", [namespaces])
