"""Role-agnostic JSON-RPC core shared by lliquidlink.client and lliquidlink.server."""
from __future__ import annotations

from ._interfaces import ByteStream
from ._rpc import (
    JsonRpcPeer,
    StdioByteStream,
    ConnectionClosedError,
    RpcError,
    encode_frame,
    decode_frame,
)
from ._timing import RpcTimingStats

__all__ = [
    "ByteStream",
    "JsonRpcPeer",
    "StdioByteStream",
    "ConnectionClosedError",
    "RpcError",
    "encode_frame",
    "decode_frame",
    "RpcTimingStats",
]
