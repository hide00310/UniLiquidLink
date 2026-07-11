"""Lazy proxies for Unity object property/method access over RPC."""
from __future__ import annotations
from typing import Any, List

from .models import RpcChainStep

def _resolve_arg(arg):
    if isinstance(arg, PropertyProxy):
        return arg()
    else:
        return arg

def _resolve_args(args: List):
    ret = []
    for arg in args:
        ret.append(_resolve_arg(arg))
    return ret

class ObjectProxy:
    """Proxy for a live Unity object; attribute access builds RPC chains.

    The raw server descriptor is kept on ``data``; garbage collection of this
    proxy schedules a batched release of the underlying Unity object.
    """

    def __init__(self, client, data: dict):
        object.__setattr__(self, "_client", client)
        object.__setattr__(self, "data", data)
        client._track_release(self, data)

    def __getattr__(self, name: str) -> Any:
        if name.startswith("_"):
            raise AttributeError(name)
        return PropertyProxy(self._client, self.data, [name])

    def __setattr__(self, name: str, value: Any) -> None:
        if name.startswith("_") or name == "data":
            object.__setattr__(self, name, value)
            return
        value = _resolve_arg(value)
        self._client._call_sync("JsonRpc_ResolveChainSet", [self.data, [], name, value])

    def __repr__(self) -> str:
        return "ObjectProxy(%r)" % (self.data,)


class PropertyProxy:
    """Accumulates a property/method chain, resolved server-side when called.

    Examples::

        obj.prop                       # proxy
        obj.prop()                     # resolved value
        obj.parent.child               # chained getter
        obj.GetComponent('Foo')        # chained method call
    """

    def __init__(self, client, obj, chain: List[str]):
        object.__setattr__(self, "_client", client)
        object.__setattr__(self, "_obj", obj)
        object.__setattr__(self, "_chain", chain)

    def __getattr__(self, name: str) -> "PropertyProxy":
        if name.startswith("_"):
            raise AttributeError(name)
        return PropertyProxy(self._client, self._obj, self._chain + [name])

    def __setattr__(self, name: str, value: Any) -> None:
        if name.startswith("_"):
            object.__setattr__(self, name, value)
            return
        value = _resolve_arg(value)
        steps = [RpcChainStep(p) for p in self._chain]
        self._client._call_sync("JsonRpc_ResolveChainSet", [self._obj, steps, name, value])

    async def _resolve(self, *args) -> Any:
        """Send the accumulated chain to Unity and return the resolved value."""
        method = self._chain[-1]
        params = list(args)
        if (self._obj is None) and (len(self._chain) <= 1):
            return await self._client._call_async(method, params)
        steps = [RpcChainStep(p) for p in self._chain[:-1]]
        return await self._client._call_async(
            "JsonRpc_ResolveChain", [self._obj, steps, method, params])

    def __call__(self, *args) -> Any:
        """Resolve synchronously (worker thread) or return a coroutine (async)."""
        args = _resolve_args(args)
        return self._client._run(self._resolve(*args))

    def __await__(self):
        return self._resolve().__await__()
