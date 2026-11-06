"""Batches Unity object-release notifications triggered by Python GC."""
from __future__ import annotations
import functools
import itertools
import threading
import weakref
from typing import Any, Callable, List, Optional, Set, TYPE_CHECKING
if TYPE_CHECKING:
    from ._interfaces import Transport
    from ._client import Client


class ReleaseManager:
    """Tracks GC'd ObjectProxy instances and batches their release over RPC.

    Flushes fire-and-forget via `rpc_notify` by default; pass `verify=True`
    to flush via `rpc_call` instead and get back the instance ids Unity
    actually removed (used by integration tests to confirm the round trip).
    """

    def __init__(self, transport: Transport, verify: bool = False):
        self._transport: Transport = transport
        self._verify: bool = verify
        self._pending_releases: Set[int] = set()
        self._release_lock: threading.Lock = threading.Lock()

    def track(self, proxy) -> None:
        """Schedule a release when `proxy` is garbage-collected."""
        instance_id = proxy._instance_id
        if instance_id is None:
            return
        weakref.finalize(proxy, self._schedule, instance_id)

    def _schedule(self, instance_id: int) -> None:
        with self._release_lock:
            self._pending_releases.add(instance_id)

    async def flush_async(self) -> Optional[List[int]]:
        with self._release_lock:
            if not self._pending_releases:
                return None
            ids = sorted(self._pending_releases)
            self._pending_releases.clear()
        if self._transport.closed:
            return None
        if self._verify:
            return await self._transport.rpc_call("JsonRpc_ReleaseObjects", [ids])
        await self._transport.rpc_notify("JsonRpc_ReleaseObjects", [ids])
        return None

    def flush(self) -> Optional[List[int]]:
        """Send pending object releases now (callable from a worker thread)."""
        return self._transport.run(self.flush_async())


def gc_flush(func: Callable[..., Any]) -> Callable[..., Any]:
    """Decorator: flush pending Unity object releases after the call returns.

    The client is the first `Client` argument, so this works both on `Client`
    methods (`self` is the client) and on wrapper-class methods that receive
    the client as a later argument.
    """
    @functools.wraps(func)
    def wrapper(*args, **kwargs):
        # Imported here because _client imports this module at import time.
        from ._client import Client
        client = next(
            (a for a in itertools.chain(args, kwargs.values()) if isinstance(a, Client)),
            None)
        if client is None:
            raise TypeError(
                f"{func.__name__}() decorated with @gc_flush was called without a Client argument")
        try:
            return func(*args, **kwargs)
        finally:
            client.flush_releases()
    return wrapper
