"""
Source-of-truth schema definitions for the RPC wire protocol.

Edit this file to add or modify schema types, then run generate_schema.py
to regenerate Python~/lliquidlink/client/_schema.py and CSharp/LLiquidLink/Models/Schema.cs.
"""

from typing import List, Literal
from pydantic import BaseModel, Field, ConfigDict


class RpcType(BaseModel):
    """Represents a .NET Type reference transmitted as a JSON-RPC parameter."""

    value: str
    rpcType: int = Field(1)

class RpcChainStep(BaseModel):
    """Single step in a property/method chain resolved server-side."""

    name: str

class _InstanceObjectAttr(BaseModel):
    instanceObjectAttr: int = Field(0)

class RpcUnityObject(_InstanceObjectAttr):
    """JSON-serializable descriptor for a live Unity Object instance."""

    rpcType: str
    instanceId: int
    orgType: str
    name: str

class ReleaseRequest(BaseModel):
    """Batch request to release Unity object references held by the server."""

    data_list: List[dict]
    action: Literal["release_objects"] = Field("release_objects", alias="action")


class RpcEnum(BaseModel):
    """Represents a .NET Type reference transmitted as a JSON-RPC parameter."""

    value: str
    rpcEnum: int = Field(1)

# ---------------------------------------------------------------------------
# Metadata consumed by generate_schema.py
# ---------------------------------------------------------------------------

# Models to generate into CSharp/LLiquidLink/Models/Schema.cs via Quicktype.
# RpcUnityObject is excluded: its C# counterpart uses RpcObjectBase inheritance
# which Quicktype cannot represent (would produce duplicate attributes field).
CS_MODELS = [RpcType, RpcChainStep, RpcUnityObject, RpcEnum]

# Models to generate into Python~/lliquidlink/client/_schema.py directly (not via Quicktype).
PY_MODELS = [RpcType, RpcChainStep, ReleaseRequest, RpcEnum]

# Override C# field types after Quicktype generation.
# Quicktype maps JSON Schema 'integer' -> C# 'long'; override specific fields here.
# Format: {('ClassName', 'wire_field_name'): 'cs_type'}
CS_FIELD_TYPE_OVERRIDES: dict = {}
