from __future__ import annotations

from ._schema import *

def type_(value: str):
    """Create an RpcType parameter for passing a .NET Type to a Unity RPC method.

    Args:
        value: Fully qualified .NET type name, e.g. ``"UnityEngine.GameObject"``.

    Returns:
        An RpcType instance wrapping the given type name.
    """
    return RpcType(value)

def enum(value: str):
    return RpcEnum(value)
