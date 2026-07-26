"""
Source-of-truth schema definitions for the RPC wire protocol.

Edit this file to add or modify schema types, then run generate_schema.py
to regenerate Python~/lliquidlink/client/_schema.py and CSharp/LLiquidLink/Models/Schema.cs.
"""

from typing import List, Literal, Any, Optional, ClassVar
from pydantic import BaseModel as B, Field, ConfigDict

class RpcType(B):
    """Represents a .NET Type reference transmitted as a JSON-RPC parameter."""

    value: str
    rpcType: int = Field(1)

class RpcChainStep(B):
    """Single step in a property/method chain resolved server-side."""

    name: str

class _InstanceObjectAttr(B):
    instanceObjectAttr: int = Field(0)

class RpcInstanceObject(_InstanceObjectAttr):
    """JSON-serializable descriptor for a live Instance Object."""

    rpcType: str
    instanceId: int
    orgType: str
    name: str

class RpcEnum(B):
    """Represents a .NET enum reference transmitted as a JSON-RPC parameter."""

    value: str
    rpcEnum: int = Field(1)

class RpcRequest(B):
    method: str
    id: Optional[int] = None
    params: List[Any]

class RpcResolveChainParam(B):
    obj: Any
    steps: List[RpcChainStep]
    method: str
    args: List[Any]

class RpcResolveChainSetParam(B):
    obj: Any
    steps: List[RpcChainStep]
    property: str
    value: Any

# ---------------------------------------------------------------------------
# Metadata consumed by generate_schema.py
# ---------------------------------------------------------------------------

# Models to generate into CSharp/LLiquidLink/Models/Schema.cs via Quicktype.
# RpcUnityObject is excluded: its C# counterpart uses RpcObjectBase inheritance
# which Quicktype cannot represent (would produce duplicate attributes field).
CS_MODELS = [
    RpcType, RpcChainStep, RpcInstanceObject, 
    RpcEnum, RpcRequest, RpcResolveChainParam, RpcResolveChainSetParam,
]

# Models to generate into Python~/lliquidlink/client/_schema.py directly (not via Quicktype).
PY_MODELS = [
    RpcType, RpcChainStep, 
    RpcEnum, RpcResolveChainParam, RpcResolveChainSetParam,
]

# Override C# field types after Quicktype generation.
# Quicktype maps JSON Schema 'integer' -> C# 'long'; override specific fields here.
# Format: {'org_type': 'cs_type'}
CS_FIELD_TYPE_OVERRIDES: dict = {
    "object" : "System.Text.Json.JsonElement",
}
