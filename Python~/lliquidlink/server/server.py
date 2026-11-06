"""Server: owns the external client transport and the C# IPC bridge.

Both legs use the same 4-byte length-prefixed JSON-RPC framing; the frame codec
is shared with the client core (lliquidlink.core).
"""
from __future__ import annotations

import asyncio
import logging
import argparse
import time
from typing import Optional

from ..core import encode_frame, decode_frame, RpcTimingStats
from .ipc_bridge import IpcBridge
from .resolver import RpcNameResolver, TypeNameResolver
from ._interfaces import MessageStream, RpcBridge
from ._transport import ServerTransport, TcpServerTransport

logger = logging.getLogger(__name__)

# Methods handled locally by the gateway; not forwarded to C#.
_GATEWAY_METHODS = {"add_abbreviated_classes", "add_abbreviated_namespaces", "get_rpc_timing_stats"}

# Methods whose params carry a server-side resolution chain in params[0]["steps"].
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


async def _handle_client(
    ws: MessageStream,
    bridge: RpcBridge,
    resolver: RpcNameResolver,
    type_resolver: Optional[TypeNameResolver] = None,
) -> None:
    """Handle one external client connection: forward all requests to C# via ipc_bridge.

    Runs inside the listener's per-connection task; any exception left to propagate out of
    this function (e.g. from `ws.send` after the peer disconnects) would otherwise crash the
    whole listener's task group, so failures are caught and logged here instead, ending only
    this one connection.
    """
    try:
        await _handle_client_messages(ws, bridge, resolver, type_resolver)
    except Exception:
        logger.exception("client handler error")


async def _handle_client_messages(
    ws: MessageStream,
    bridge: RpcBridge,
    resolver: RpcNameResolver,
    type_resolver: Optional[TypeNameResolver] = None,
) -> None:
    # RPC timing for this one external connection; always on (no per-connection
    # opt-out on the server side), reset implicitly since it's created fresh here.
    # Byte counting (record_bytes) covers every frame on this connection, including
    # the gateway-local methods below; the time buckets (record) only cover the main
    # forwarded-RPC branch further down, matching the existing rpc_count scope.
    external_timing = RpcTimingStats(enabled=True)

    async for raw in ws:
        request_start = time.perf_counter()
        received_wall = time.time()
        external_timing.record_bytes(received=len(raw))
        try:
            msg = decode_frame(raw)
            logger.debug("recv: %s", msg)
        except Exception:
            logger.exception("ws parse error")
            continue

        msg_id = msg.get("id")
        method = msg.get("method", "")
        params = msg.get("params", [])
        # Wall-clock send timestamp the client embedded (only present when the
        # client has RPC timing enabled); used below for request_network_time
        # and to decide whether to echo one back on the response.
        sent_at = msg.get("sent_at")

        if method == "get_rpc_timing_stats":
            if msg_id is not None:
                # Round-trips through the IPC leg (C# echoes "sent_at" on every response, so this
                # call also exercises the same wire path being measured, same as any other RPC).
                csharp_stats = await bridge.call("GetRpcTimingStats", [])
                stats = {"external": external_timing.to_dict(), "ipc": bridge.timing_stats.to_dict(),
                         "csharp": csharp_stats}
                response = {"jsonrpc": "2.0", "id": msg_id, "result": stats}
                frame = encode_frame(response)
                external_timing.record_bytes(sent=len(frame))
                await ws.send(frame)
            continue

        if method == "add_abbreviated_classes":
            resolver.add_abbreviated_classes(params[0] if params else [])
            if msg_id is not None:
                response = {"jsonrpc": "2.0", "id": msg_id, "result": None}
                frame = encode_frame(response)
                external_timing.record_bytes(sent=len(frame))
                await ws.send(frame)
            continue

        if method == "add_abbreviated_namespaces":
            if type_resolver is not None:
                type_resolver.add_abbreviated_namespaces(params[0] if params else [])
            if msg_id is not None:
                response = {"jsonrpc": "2.0", "id": msg_id, "result": None}
                frame = encode_frame(response)
                external_timing.record_bytes(sent=len(frame))
                await ws.send(frame)
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
            # Awaited (not the fire-and-forget bridge.notify) so this notification is written to
            # the C# stdio stream before any later request/notification from this connection,
            # and so a send failure surfaces here instead of being lost on an untracked task.
            await bridge.anotify(resolved, params)
            continue

        resolved = method if method in _SKIP_RESOLVE else resolver.resolve(method)
        remote_start = time.perf_counter()
        try:
            logger.debug("bridge.call: %s(%s)", resolved, params)
            result = await bridge.call(resolved, params)
            response = {"jsonrpc": "2.0", "id": msg_id, "result": result}
        except Exception as e:
            response = {"jsonrpc": "2.0", "id": msg_id,
                        "error": {"code": -32603, "message": str(e)}}
            logger.exception("bridge.call error")
        remote_elapsed = time.perf_counter() - remote_start
        # Only echo "sent_at"/"received_at" back when the request carried a "sent_at":
        # external_timing is always enabled on the gateway, so gating on that (rather
        # than on whether the client actually opted in) would add new response fields
        # for every client, including ones with RPC timing turned off.
        if sent_at is not None:
            response["sent_at"] = time.time()
            response["received_at"] = received_wall
        logger.debug("send: %s", response)
        frame = encode_frame(response)
        external_timing.record_bytes(sent=len(frame))
        await ws.send(frame)
        # local_processing_time = decode + name/type resolution + response encode +
        # send, i.e. everything in this request's window except the bridge.call wait
        # (remote_time). request_network_time covers the period strictly before
        # request_start (client encode+send + wire transit + queueing until this frame
        # was read), so unlike the two perf_counter buckets it is not derived from them.
        # response_network_time stays 0.0: this leg has no way to measure the client's
        # own receipt time of this response.
        request_network_time = (received_wall - sent_at) if sent_at is not None else 0.0
        external_timing.record(
            local_processing_time=(time.perf_counter() - request_start) - remote_elapsed,
            remote_time=remote_elapsed,
            request_network_time=request_network_time)


class Server:
    """Owns both legs of the bridge: the external ServerTransport and the C# IpcBridge.

    - external leg: `transport` (ServerTransport) accepts Python-client connections
      (TCP by default, or any other ServerTransport implementation).
    - internal leg: `bridge` (IpcBridge) talks to the C# Unity process over stdio.
    Each external request is resolved (abbreviated class/type names, chain collapsing)
    then forwarded to `bridge`.
    """

    def __init__(self, data_dir: str, transport: ServerTransport, bridge: Optional[IpcBridge] = None,
                 rpc_names_csv: str = "rpc_names.csv", type_names_csv: str = "type_names.csv",
                 resolver: Optional[RpcNameResolver] = None, type_resolver: Optional[TypeNameResolver] = None):
        self._transport: ServerTransport = transport
        self._bridge: IpcBridge = bridge if bridge is not None else IpcBridge()
        self._resolver: RpcNameResolver = resolver if resolver is not None else RpcNameResolver(data_dir + "/" + rpc_names_csv)
        self._type_resolver: TypeNameResolver = type_resolver if type_resolver is not None else TypeNameResolver(data_dir + "/" + type_names_csv)

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
    parser = argparse.ArgumentParser()
    parser.add_argument('-dataDir', type=str)
    args = parser.parse_args()
    asyncio.run(Server(args.dataDir, transport=TcpServerTransport()).serve())
