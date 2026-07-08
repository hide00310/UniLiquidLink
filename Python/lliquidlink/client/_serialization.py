"""JSON encode/decode hooks bridging Python values and the JSON-RPC wire format."""
from __future__ import annotations
import dataclasses
from typing import Any

from ._proxy import ObjectProxy

class Serialization:
    """Namespace for the json.dumps default and json.loads object_hook factories."""
    def __init__(self, make_object_proxy):
        self._make_object_proxy = make_object_proxy

    @staticmethod
    def encode_default(obj: Any) -> Any:
        """json.dumps default: ObjectProxy -> raw dict; dataclass -> dict."""
        if isinstance(obj, ObjectProxy):
            return obj.data
        if dataclasses.is_dataclass(obj) and not isinstance(obj, type):
            return dataclasses.asdict(obj)
        raise TypeError("Object of type %s is not JSON serializable" % type(obj).__name__)

    def object_hook(self, d: dict):
        """Build a json.loads object_hook: InstanceObject dict -> ObjectProxy."""
        if "instanceObjectAttr" in d.keys():
            return self._make_object_proxy(d)
        return d
