"""Identity map ensuring one live ObjectProxy per Unity-side instanceId."""
from __future__ import annotations
import weakref
from typing import Any, Callable, Dict, TYPE_CHECKING
if TYPE_CHECKING:
    from ._proxy import ObjectProxy


class ObjectIdentityCache:
    """Weakly-referenced instanceId -> ObjectProxy map.

    RPC calls are never skipped; this only ensures repeated resolutions of the
    same Unity-side object reuse a single Python proxy, so Python's own
    refcounting/GC drives release timing instead of per-proxy finalizers
    racing each other. Lookup/insert only happens on the single decode thread
    (see JsonRpcPeer.serve), so no lock is needed here.
    """

    def __init__(self) -> None:
        self._entries: "weakref.WeakValueDictionary[int, ObjectProxy]" = weakref.WeakValueDictionary()

    def get_or_create(
        self,
        data: Dict[str, Any],
        factory: Callable[[Dict[str, Any]], "ObjectProxy"],
    ) -> "ObjectProxy":
        """Return the live proxy for data's instanceId, creating one via factory on a miss."""
        instance_id = data.get("instanceId")
        if instance_id is not None:
            cached = self._entries.get(instance_id)
            if cached is not None:
                return cached
        proxy = factory(data)
        if instance_id is not None:
            self._entries[instance_id] = proxy
        return proxy
