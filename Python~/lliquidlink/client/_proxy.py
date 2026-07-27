"""Lazy proxies for Unity object property/method access over RPC."""
from __future__ import annotations
from typing import Any, Callable, Dict, List, Optional, Tuple, TYPE_CHECKING
from .models import RpcChainStep, RpcResolveChainParam, RpcResolveChainSetParam
from ._interfaces import SupportsAsDict
if TYPE_CHECKING:
    from ._interfaces import Transport

def _resolve_arg(arg):
    if isinstance(arg, PropertyProxy):
        return arg()
    else:
        return arg

def _resolve_args(args: List[Any]) -> List[Any]:
    ret = []
    for arg in args:
        ret.append(_resolve_arg(arg))
    return ret

class ObjectProxy(SupportsAsDict):
    """Proxy for a live Unity object; attribute access builds RPC chains.

    The raw server descriptor is kept on ``data``; garbage collection of this
    proxy schedules a batched release of the underlying Unity object.
    """

    data: Dict[str, Any]
    _transport: Transport
    _make_property_proxy: Callable[[Dict[str, Any], List[str]], PropertyProxy]

    def __init__(
        self,
        data: Dict[str, Any],
        transport: Transport,
        track: Callable[[ObjectProxy, Dict[str, Any]], None],
        make_property_proxy: Callable[[Dict[str, Any], List[str]], PropertyProxy],
    ):
        object.__setattr__(self, "data", data)
        object.__setattr__(self, "_transport", transport)
        object.__setattr__(self, "_make_property_proxy", make_property_proxy)
        track(self, data)

    def __getattr__(self, name: str) -> PropertyProxy:
        if name.startswith("_"):
            raise AttributeError(name)
        return self._make_property_proxy(self.data, [name])

    def __setattr__(self, name: str, value) -> None:
        if name.startswith("_") or name == "data":
            object.__setattr__(self, name, value)
            return
        value = _resolve_arg(value)
        param = RpcResolveChainSetParam(obj=self.data, steps=[], property=name, value=value)
        self._transport.call_sync("JsonRpc_ResolveChainSet", [param])

    def __repr__(self) -> str:
        return "ObjectProxy(%r)" % (self.data,)

    def _asdict(self) -> Dict[str, Any]:
        """Wire representation for JSON encoding: the raw server object descriptor."""
        return self.data


class PropertyProxy:
    """Accumulates a property/method chain, resolved server-side when called.

    Examples::

        obj.prop                       # proxy
        obj.prop()                     # resolved value
        obj.parent.child               # chained getter
        obj.GetComponent('Foo')        # chained method call
    """

    _obj: Optional[Dict[str, Any]]
    _chain: List[str]
    _transport: Transport
    _make_property_proxy: Callable[[Dict[str, Any], List[str]], PropertyProxy]

    def __init__(
        self,
        obj: Optional[Dict[str, Any]],
        chain: List[str],
        transport: Transport,
        make_property_proxy: Callable[[Dict[str, Any], List[str]], PropertyProxy],
    ):
        object.__setattr__(self, "_obj", obj)
        object.__setattr__(self, "_chain", chain)
        object.__setattr__(self, "_transport", transport)
        object.__setattr__(self, "_make_property_proxy", make_property_proxy)

    def __getattr__(self, name: str) -> PropertyProxy:
        if name.startswith("_"):
            raise AttributeError(name)
        return self._make_property_proxy(self._obj, self._chain + [name])

    def __setattr__(self, name: str, value) -> None:
        if name.startswith("_"):
            object.__setattr__(self, name, value)
            return
        value = _resolve_arg(value)
        steps = [RpcChainStep(p) for p in self._chain]
        param = RpcResolveChainSetParam(obj=self._obj, steps=steps, property=name, value=value)
        self._transport.call_sync("JsonRpc_ResolveChainSet", [param])

    def _chain_target(self, params: List[Any]) -> Tuple[str, List[Any]]:
        """Compute the (method, params) RPC target for the accumulated chain (pure, no I/O)."""
        method = self._chain[-1]
        if (self._obj is None) and (len(self._chain) <= 1):
            return method, params
        steps = [RpcChainStep(p) for p in self._chain[:-1]]
        param = RpcResolveChainParam(obj=self._obj, steps=steps, method=method, args=params)
        return "JsonRpc_ResolveChain", [param]

    async def _resolve(self, *args):
        """Send the accumulated chain to Unity and return the resolved value."""
        method, params = self._chain_target(list(args))
        return await self._transport.rpc_call(method, params)

    def __call__(self, *args):
        """Resolve synchronously via the transport's call_sync bridge."""
        args = _resolve_args(args)
        method, params = self._chain_target(args)
        return self._transport.call_sync(method, params)

    def __await__(self):
        return self._resolve().__await__()
