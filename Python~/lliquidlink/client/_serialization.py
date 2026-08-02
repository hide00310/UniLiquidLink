"""JSON encode/decode hooks bridging Python values and the JSON-RPC wire format."""
from __future__ import annotations
import dataclasses
from typing import Any, Callable, Dict


class Serialization:
    """Namespace for the json.dumps default and json.loads object_hook factories."""
    def __init__(self, make_object_proxy: Callable[[Dict[str, Any]], Any]):
        self._make_object_proxy: Callable[[Dict[str, Any]], Any] = make_object_proxy

    @staticmethod
    def encode_default(obj) -> Dict[str, Any]:
        """json.dumps default: dataclass -> shallow field dict; any other object
        exposing _asdict() (e.g. ObjectProxy) -> that dict. json.dumps recurses into
        the dict/list values returned here, so nested dataclasses/proxies at deeper
        leaves are re-encoded automatically without manual recursion."""
        if dataclasses.is_dataclass(obj) and not isinstance(obj, type):
            return {f.name: getattr(obj, f.name) for f in dataclasses.fields(obj)}
        as_dict = getattr(obj, "_asdict", None)
        if as_dict is not None:
            return as_dict()
        raise TypeError("Object of type %s is not JSON serializable" % type(obj).__name__)

    def object_hook(self, d: Dict[str, Any]):
        """Build a json.loads object_hook: InstanceObject dict -> ObjectProxy."""
        if "instanceObjectAttr" in d.keys():
            return self._make_object_proxy(d)
        return d
