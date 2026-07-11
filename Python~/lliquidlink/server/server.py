"""Server: owns the external client transport and the C# IPC bridge.

Both legs use the same 4-byte length-prefixed JSON-RPC framing; the frame codec
is shared with the client core (lliquidlink.core).
"""
from __future__ import annotations

import asyncio
import logging

from ..core import encode_frame, decode_frame
from .ipc_bridge import IpcBridge
from .resolver import RpcNameResolver, TypeNameResolver
from ._transport import ServerTransport, TcpServerTransport

logger = logging.getLogger(__name__)

# Methods handled locally by the gateway; not forwarded to C#.
_GATEWAY_METHODS = {"add_abbreviated_classes", "add_abbreviated_namespaces"}

# Methods whose params carry a server-side resolution chain in params[1].
_CHAIN_METHODS = {"JsonRpc_ResolveChain", "JsonRpc_ResolveChainSet"}

# Methods forwarded as-is without method-name resolution.
_SKIP_RESOLVE = {"JsonRpc_ResolveChain", "JsonRpc_ResolveChainSet", "release_objects"}


def _resolve_types_in_value(value, type_resolver: TypeNameResolver):
    """Recursively resolve RpcType dicts ({"rpcType": 1, "value": "..."}) in a JSON value."""
    if isinstance(value, dict):
        if "rpcType" in value and "value" in value and isinstance(value["value"], str):
            resolved = type_resolver.resolve(value["value"])
            return {**value, "value": resolved}
        return {k: _resolve_types_in_value(v, type_resolver) for k, v in value.items()}
    if isinstance(value, list):
        return [_resolve_types_in_value(v, type_resolver) for v in value]
    return value


async def _handle_client(ws, bridge, resolver: RpcNameResolver, type_resolver: TypeNameResolver = None):
    """Handle one external client connection: forward all requests to C# via ipc_bridge."""
    async for raw in ws:
        try:
            msg = decode_frame(raw)
            logger.debug("recv: %s", msg)
        except Exception as e:
            logger.error("ws parse error: %s", e)
            continue

        msg_id = msg.get("id")
        method = msg.get("method", "")
        params = msg.get("params", [])

        if method == "add_abbreviated_classes":
            resolver.add_abbreviated_classes(params[0] if params else [""])
            if msg_id is not None:
                response = {"jsonrpc": "2.0", "id": msg_id, "result": None}
                await ws.send(encode_frame(response))
            continue

        if method == "add_abbreviated_namespaces":
            if type_resolver is not None:
                type_resolver.add_abbreviated_namespaces(params[0] if params else [])
            if msg_id is not None:
                response = {"jsonrpc": "2.0", "id": msg_id, "result": None}
                await ws.send(encode_frame(response))
            continue

        # Resolve short .NET type names in params before forwarding to C#.
        if type_resolver is not None and method not in _GATEWAY_METHODS:
            params = _resolve_types_in_value(params, type_resolver)

        # Resolve abbreviated/static names in a ResolveChain* call.
        if method in _CHAIN_METHODS:
            collapsed = resolver.try_collapse_static_chain(method, params)
            if collapsed is not None:
                method, params = collapsed
            else:
                params = resolver.resolve_chain_params(params)

        if msg_id is None:
            resolved = method if method in _SKIP_RESOLVE else resolver.resolve(method)
            bridge.notify(resolved, params)
            continue

        resolved = method if method in _SKIP_RESOLVE else resolver.resolve(method)
        try:
            logger.debug("bridge.call: %s(%s)", resolved, params)
            result = await bridge.call(resolved, params)
            response = {"jsonrpc": "2.0", "id": msg_id, "result": result}
        except Exception as e:
            response = {"jsonrpc": "2.0", "id": msg_id,
                        "error": {"code": -32603, "message": str(e)}}
            logger.error("bridge.call error: %s", e)
        logger.debug("send: %s", response)
        await ws.send(encode_frame(response))


class Server:
    """Owns both legs of the bridge: the external ServerTransport and the C# IpcBridge.

    - external leg: `transport` (ServerTransport) accepts Python-client connections
      (TCP by default, or any other ServerTransport implementation).
    - internal leg: `bridge` (IpcBridge) talks to the C# Unity process over stdio.
    Each external request is resolved (abbreviated class/type names, chain collapsing)
    then forwarded to `bridge`.
    """

    def __init__(self, transport: ServerTransport, bridge: IpcBridge = None,
                 rpc_names_csv: str = "rpc_names.csv", type_names_csv: str = "type_names.csv",
                 resolver: RpcNameResolver = None, type_resolver: TypeNameResolver = None):
        self._transport = transport
        self._bridge = bridge if bridge is not None else IpcBridge()
        self._resolver = resolver if resolver is not None else RpcNameResolver(rpc_names_csv)
        self._type_resolver = type_resolver if type_resolver is not None else TypeNameResolver(type_names_csv)

    async def serve(self) -> None:
        await self._bridge.connect()
        try:
            await self._transport.serve(
                lambda ws: _handle_client(ws, self._bridge, self._resolver, self._type_resolver))
        except Exception as e:
            logger.error("Server error: %s", e)
            try:
                await self._bridge.anotify("OnServerError", [str(e)])
            except Exception:
                pass

    async def stop(self) -> None:
        await self._transport.stop()


def main() -> None:
    logger.info("start")
    asyncio.run(Server(TcpServerTransport()).serve())
