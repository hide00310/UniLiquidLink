"""Role-agnostic JSON-RPC core shared by lliquidlink.client and lliquidlink.server."""
from __future__ import annotations

from ._rpc import (
    JsonRpcPeer,
    StdioByteStream,
    ConnectionClosedError,
    RpcError,
    encode_frame,
    decode_frame,
)

__all__ = [
    "JsonRpcPeer",
    "StdioByteStream",
    "ConnectionClosedError",
    "RpcError",
    "encode_frame",
    "decode_frame",
]
