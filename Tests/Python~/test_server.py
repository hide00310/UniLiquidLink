"""Unit tests for lliquidlink.server (server + ipc_bridge) without C#."""
import time

import anyio
import pytest
from anyio.streams.buffered import BufferedByteReceiveStream

from lliquidlink.core import encode_frame, decode_frame, RpcTimingStats
from lliquidlink.server._transport import TcpServerTransport
from lliquidlink.server.ipc_bridge import IpcBridge
from lliquidlink.server.server import _handle_client, _resolve_types_in_value
from lliquidlink.server.resolver import RpcNameResolver, TypeNameResolver


# ── 4-byte framing round trip ─────────────────────────────────────────────────

def test_frame_roundtrip():
    msg = {"jsonrpc": "2.0", "id": 1, "result": 42}
    assert decode_frame(encode_frame(msg)) == msg


# ── ws_gateway._handle_client ─────────────────────────────────────────────────

class FakeWebSocket:
    """Feeds pre-canned 4-byte frames and records sent frames."""

    def __init__(self, messages):
        self._messages = iter(messages)
        self.sent = []

    def __aiter__(self):
        return self

    async def __anext__(self):
        try:
            return next(self._messages)
        except StopIteration:
            raise StopAsyncIteration

    async def send(self, data):
        self.sent.append(data)


class FakeBridge:
    """Returns canned results and records calls and notifications."""

    # Default stand-in for the real C#-side RpcTimingStats.ToDict() response, used unless a test
    # overrides "GetRpcTimingStats" in its own results dict.
    DEFAULT_CSHARP_STATS = {
        "rpc_count": 0, "local_processing_time": 0.0, "communication_time": 0.0,
        "remote_time": 0.0, "total_time": 0.0,
        "response_network_time": 0.0, "request_network_time": 0.0,
        "bytes_sent": 0, "bytes_received": 0,
        "dispatch_queue_time": 0.0, "decode_time": 0.0, "resolve_time": 0.0,
        "deserialize_args_time": 0.0, "invoke_time": 0.0, "serialize_result_time": 0.0,
        "encode_time": 0.0, "send_time": 0.0, "overload_attempt_count": 0,
    }

    def __init__(self, results):
        self._results = results
        self.calls = []
        self.notifications = []
        self.timing_stats = RpcTimingStats(enabled=True)

    async def call(self, method, params):
        self.calls.append((method, params))
        if method not in self._results:
            if method == "GetRpcTimingStats":
                return self.DEFAULT_CSHARP_STATS
            raise RuntimeError("Method not found: " + method)
        return self._results[method]

    async def anotify(self, method, params):
        self.notifications.append((method, params))


def _resolver():
    return RpcNameResolver("__nonexistent__")


def test_rpc_name_resolver_missing_csv_logs_error(caplog):
    with caplog.at_level("ERROR", logger="lliquidlink.server.resolver"):
        RpcNameResolver("__nonexistent__")
    assert any(rec.levelname == "ERROR" and "RPC names CSV not found" in rec.message
               for rec in caplog.records)


def _resolver_with_entries(entries: dict) -> RpcNameResolver:
    """Build RpcNameResolver pre-populated with {(class_name, method_name): full_name}."""
    import pandas as pd
    r = RpcNameResolver("__nonexistent__")
    rows = [{"class_name": cls, "method_name": method, "full_name": full}
            for (cls, method), full in entries.items()]
    r._lookup = pd.DataFrame(rows).set_index(["class_name", "method_name"])
    return r


@pytest.mark.asyncio
async def test_handle_client_rpc_call():
    ws = FakeWebSocket([encode_frame(
        {"jsonrpc": "2.0", "id": 7, "method": "SampleMethodInt", "params": [5]})])
    await _handle_client(ws, FakeBridge({"SampleMethodInt": 5}), _resolver())
    response = decode_frame(ws.sent[0])
    assert response["id"] == 7
    assert response["result"] == 5


@pytest.mark.asyncio
async def test_handle_client_rpc_error():
    ws = FakeWebSocket([encode_frame(
        {"jsonrpc": "2.0", "id": 9, "method": "Unknown", "params": []})])
    await _handle_client(ws, FakeBridge({}), _resolver())
    response = decode_frame(ws.sent[0])
    assert response["id"] == 9
    assert "error" in response


@pytest.mark.asyncio
async def test_handle_client_notification():
    ws = FakeWebSocket([encode_frame(
        {"jsonrpc": "2.0", "method": "release_objects", "params": ["[1,2]"]})])
    bridge = FakeBridge({})
    await _handle_client(ws, bridge, _resolver())
    assert ws.sent == []
    assert bridge.notifications == [("release_objects", ["[1,2]"])]


@pytest.mark.asyncio
async def test_handle_client_add_abbreviated_namespaces():
    r = _type_resolver_with(["UnityEngine.Object", "System.Object"])
    ws = FakeWebSocket([encode_frame(
        {"jsonrpc": "2.0", "id": 2, "method": "add_abbreviated_namespaces",
         "params": [["UnityEngine"]]})])
    await _handle_client(ws, FakeBridge({}), _resolver(), r)
    response = decode_frame(ws.sent[0])
    assert response["id"] == 2
    assert response["result"] is None
    assert "UnityEngine" in r._abbreviated_namespaces


@pytest.mark.asyncio
async def test_handle_client_get_rpc_timing_stats():
    """get_rpc_timing_stats returns combined external+ipc+csharp stats, fetching the C# leg via bridge.call."""
    ws = FakeWebSocket([
        encode_frame({"jsonrpc": "2.0", "id": 1, "method": "SampleMethodInt", "params": [5]}),
        encode_frame({"jsonrpc": "2.0", "id": 2, "method": "get_rpc_timing_stats", "params": []}),
    ])
    bridge = FakeBridge({"SampleMethodInt": 5})
    await _handle_client(ws, bridge, _resolver())

    response = decode_frame(ws.sent[1])
    assert response["id"] == 2
    assert response["result"]["external"]["rpc_count"] == 1
    assert response["result"]["ipc"] == bridge.timing_stats.to_dict()
    assert response["result"]["csharp"] == FakeBridge.DEFAULT_CSHARP_STATS
    # Fetching stats now also forwards a "GetRpcTimingStats" call to C# to get its own leg's stats.
    assert [c[0] for c in bridge.calls] == ["SampleMethodInt", "GetRpcTimingStats"]


@pytest.mark.asyncio
async def test_handle_client_request_network_time_and_response_echoes_sent_at():
    """A request carrying sent_at yields request_network_time > 0 and gets sent_at echoed back."""
    req1 = encode_frame({"jsonrpc": "2.0", "id": 1, "method": "SampleMethodInt", "params": [5],
                          "sent_at": time.time() - 0.01})
    ws = FakeWebSocket([
        req1,
        encode_frame({"jsonrpc": "2.0", "id": 2, "method": "get_rpc_timing_stats", "params": []}),
    ])
    bridge = FakeBridge({"SampleMethodInt": 5})
    await _handle_client(ws, bridge, _resolver())

    main_response = decode_frame(ws.sent[0])
    assert "sent_at" in main_response
    assert "received_at" in main_response

    stats_response = decode_frame(ws.sent[1])
    assert stats_response["result"]["external"]["request_network_time"] > 0.0


@pytest.mark.asyncio
async def test_handle_client_records_local_and_remote_time_split():
    """remote_time covers the bridge.call wait; local_processing_time covers decode + resolve +
    response encode/send. Recording now happens after ws.send (was before), so this also
    guards against local_processing_time silently excluding the encode/send it should cover."""
    ws = FakeWebSocket([
        encode_frame({"jsonrpc": "2.0", "id": 1, "method": "SampleMethodInt", "params": [5]}),
        encode_frame({"jsonrpc": "2.0", "id": 2, "method": "get_rpc_timing_stats", "params": []}),
    ])
    bridge = FakeBridge({"SampleMethodInt": 5})
    await _handle_client(ws, bridge, _resolver())

    stats = decode_frame(ws.sent[1])["result"]["external"]
    assert stats["remote_time"] >= 0.0
    assert stats["local_processing_time"] >= 0.0
    assert stats["total_time"] == pytest.approx(
        stats["local_processing_time"] + stats["communication_time"] + stats["remote_time"])


@pytest.mark.asyncio
async def test_handle_client_response_has_no_sent_at_when_request_lacks_one():
    """Gating fix: the gateway must not add a new payload field for clients with timing off."""
    ws = FakeWebSocket([encode_frame(
        {"jsonrpc": "2.0", "id": 1, "method": "SampleMethodInt", "params": [5]})])
    await _handle_client(ws, FakeBridge({"SampleMethodInt": 5}), _resolver())
    response = decode_frame(ws.sent[0])
    assert "sent_at" not in response
    assert "received_at" not in response


@pytest.mark.asyncio
async def test_handle_client_records_bytes_across_receive_and_send():
    req1 = encode_frame({"jsonrpc": "2.0", "id": 1, "method": "SampleMethodInt", "params": [5]})
    req2 = encode_frame({"jsonrpc": "2.0", "id": 2, "method": "get_rpc_timing_stats", "params": []})
    ws = FakeWebSocket([req1, req2])
    bridge = FakeBridge({"SampleMethodInt": 5})
    await _handle_client(ws, bridge, _resolver())

    stats = decode_frame(ws.sent[1])["result"]["external"]
    # Both requests have already crossed the wire by the time this snapshot is taken.
    assert stats["bytes_received"] == len(req1) + len(req2)
    # Only the main RPC's own response was sent before this snapshot (get_rpc_timing_stats
    # never counts its own not-yet-sent response, mirroring the existing rpc_count exclusion).
    assert stats["bytes_sent"] == len(ws.sent[0])


@pytest.mark.asyncio
async def test_handle_client_records_bytes_for_add_abbreviated_classes():
    req1 = encode_frame(
        {"jsonrpc": "2.0", "id": 1, "method": "add_abbreviated_classes", "params": ["MyClass"]})
    req2 = encode_frame({"jsonrpc": "2.0", "id": 2, "method": "get_rpc_timing_stats", "params": []})
    ws = FakeWebSocket([req1, req2])
    await _handle_client(ws, FakeBridge({}), _resolver())

    stats = decode_frame(ws.sent[1])["result"]["external"]
    assert stats["bytes_received"] == len(req1) + len(req2)
    assert stats["bytes_sent"] == len(ws.sent[0])


@pytest.mark.asyncio
async def test_handle_client_records_bytes_for_add_abbreviated_namespaces():
    r = _type_resolver_with(["UnityEngine.Object"])
    req1 = encode_frame({"jsonrpc": "2.0", "id": 1, "method": "add_abbreviated_namespaces",
                          "params": [["UnityEngine"]]})
    req2 = encode_frame({"jsonrpc": "2.0", "id": 2, "method": "get_rpc_timing_stats", "params": []})
    ws = FakeWebSocket([req1, req2])
    await _handle_client(ws, FakeBridge({}), _resolver(), r)

    stats = decode_frame(ws.sent[1])["result"]["external"]
    assert stats["bytes_received"] == len(req1) + len(req2)
    assert stats["bytes_sent"] == len(ws.sent[0])


@pytest.mark.asyncio
async def test_handle_client_records_bytes_for_notification():
    req1 = encode_frame({"jsonrpc": "2.0", "method": "release_objects", "params": ["[1,2]"]})
    req2 = encode_frame({"jsonrpc": "2.0", "id": 2, "method": "get_rpc_timing_stats", "params": []})
    ws = FakeWebSocket([req1, req2])
    bridge = FakeBridge({})
    await _handle_client(ws, bridge, _resolver())

    # The notification produced no response, so the only sent frame is the stats response.
    stats = decode_frame(ws.sent[0])["result"]["external"]
    assert stats["bytes_received"] == len(req1) + len(req2)


@pytest.mark.asyncio
async def test_handle_client_set_abbreviated_class():
    resolver = _resolver()
    ws = FakeWebSocket([encode_frame(
        {"jsonrpc": "2.0", "id": 1, "method": "add_abbreviated_classes", "params": ["MyClass"]})])
    await _handle_client(ws, FakeBridge({}), resolver)
    response = decode_frame(ws.sent[0])
    assert response["id"] == 1
    assert response["result"] is None
    assert "MyClass" in resolver._abbreviated_classes


# ── TcpServerTransport over a real socket ─────────────────────────────────────

@pytest.mark.asyncio
async def test_tcp_server_transport_round_trip():
    """A real socket client should get a response through TcpServerTransport -> _handle_client."""
    host, port = "127.0.0.1", 8798
    transport = TcpServerTransport(host, port)
    bridge = FakeBridge({"SampleMethodInt": 9})

    async with anyio.create_task_group() as tg:
        tg.start_soon(transport.serve, lambda ws: _handle_client(ws, bridge, _resolver()))
        await anyio.sleep(0.05)  # let the listener bind before connecting

        stream = await anyio.connect_tcp(host, port)
        try:
            # Send the frame in small chunks to exercise the partial-read framing path.
            frame = encode_frame(
                {"jsonrpc": "2.0", "id": 1, "method": "SampleMethodInt", "params": [9]})
            for i in range(0, len(frame), 3):
                await stream.send(frame[i:i + 3])

            reader = BufferedByteReceiveStream(stream)
            header = await reader.receive_exactly(4)
            length = int.from_bytes(header, "little")
            body = await reader.receive_exactly(length)
            response = decode_frame(header + body)
            assert response["id"] == 1
            assert response["result"] == 9
        finally:
            await stream.aclose()
            await transport.stop()
            tg.cancel_scope.cancel()


# ── ipc_bridge.IpcBridge over an in-memory stream ─────────────────────────────

class LoopbackStream:
    """In-memory byte stream that echoes the first param of each request."""

    def __init__(self):
        self._buffer = bytearray()
        self._data = anyio.Event()

    async def send(self, data):
        msg = decode_frame(data)
        if msg.get("id") is None:
            return
        self._buffer.extend(encode_frame(
            {"jsonrpc": "2.0", "id": msg["id"], "result": msg["params"][0]}))
        self._data.set()

    async def receive(self, max_bytes=65536):
        while not self._buffer:
            await self._data.wait()
            self._data = anyio.Event()
        chunk = bytes(self._buffer[:max_bytes])
        del self._buffer[:max_bytes]
        return chunk

    async def aclose(self):
        pass


@pytest.mark.asyncio
async def test_ipc_bridge_call():
    bridge = IpcBridge(stream=LoopbackStream())
    await bridge.connect()
    assert await bridge.call("SampleMethodInt", [42]) == 42


# ── TypeNameResolver ──────────────────────────────────────────────────────────

def _type_resolver_with(full_names):
    """Build a TypeNameResolver pre-populated with the given FullNames (no CSV needed)."""
    r = TypeNameResolver("__nonexistent__")
    for full_name in full_names:
        r._by_full_name_lower[full_name.lower()] = full_name
        simple = full_name.rsplit(".", 1)[-1].lower()
        r._by_simple_name.setdefault(simple, []).append(full_name)
    r._loaded = True
    return r


def test_type_name_resolver_not_loaded():
    r = TypeNameResolver("__nonexistent__")
    assert r.resolve("Material") == "Material"


def test_type_name_resolver_missing_csv_logs_error(caplog):
    with caplog.at_level("ERROR", logger="lliquidlink.server.resolver"):
        TypeNameResolver("__nonexistent__")
    assert any(rec.levelname == "ERROR" and "Type names CSV not found" in rec.message
               for rec in caplog.records)


def test_type_name_resolver_exact_match():
    r = _type_resolver_with(["System.Int32", "UnityEngine.Material"])
    assert r.resolve("System.Int32") == "System.Int32"
    assert r.resolve("system.int32") == "System.Int32"  # case-insensitive


def test_type_name_resolver_simple_name_unique():
    r = _type_resolver_with(["UnityEngine.Material"])
    assert r.resolve("Material") == "UnityEngine.Material"


def test_type_name_resolver_simple_name_ambiguous():
    r = _type_resolver_with(["System.Math", "Unity.Math"])
    # Ambiguous: both have simple name "Math"; returns name unchanged
    assert r.resolve("Math") == "Math"


def test_type_name_resolver_unknown():
    r = _type_resolver_with(["UnityEngine.Material"])
    assert r.resolve("NoSuchType") == "NoSuchType"


def test_type_name_resolver_abbreviated_namespace():
    r = _type_resolver_with(["UnityEngine.Object", "System.Object"])
    r.add_abbreviated_namespaces(["UnityEngine"])
    assert r.resolve("Object") == "UnityEngine.Object"


def test_type_name_resolver_abbreviated_namespace_fallthrough():
    r = _type_resolver_with(["UnityEngine.Material"])
    r.add_abbreviated_namespaces(["System"])
    # System.Material does not exist; falls through to simple-name match
    assert r.resolve("Material") == "UnityEngine.Material"


# ── _resolve_types_in_value ───────────────────────────────────────────────────

def test_resolve_types_in_value_rpctype():
    r = _type_resolver_with(["UnityEngine.Material"])
    value = {"rpcType": 1, "value": "Material"}
    assert _resolve_types_in_value(value, r) == {"rpcType": 1, "value": "UnityEngine.Material"}


def test_resolve_types_in_value_nested_in_list():
    r = _type_resolver_with(["UnityEngine.AudioClip"])
    value = ["path/to/asset", {"rpcType": 1, "value": "AudioClip"}]
    result = _resolve_types_in_value(value, r)
    assert result[1]["value"] == "UnityEngine.AudioClip"


def test_resolve_types_in_value_already_full_name():
    r = _type_resolver_with(["UnityEngine.Material"])
    value = {"rpcType": 1, "value": "UnityEngine.Material"}
    assert _resolve_types_in_value(value, r) == {"rpcType": 1, "value": "UnityEngine.Material"}


def test_resolve_types_in_value_non_rpctype_dict_unchanged():
    r = _type_resolver_with(["UnityEngine.Material"])
    value = {"x": 1.0, "y": 2.0, "z": 3.0}
    assert _resolve_types_in_value(value, r) == {"x": 1.0, "y": 2.0, "z": 3.0}


# ── _handle_client with type resolution ──────────────────────────────────────

@pytest.mark.asyncio
async def test_handle_client_resolves_type_in_params():
    """Type name in params should be resolved to FullName before forwarding to C#."""
    r = _type_resolver_with(["UnityEngine.AudioClip"])
    frame = encode_frame({
        "jsonrpc": "2.0", "id": 10, "method": "AssetDatabase.LoadAssetAtPath",
        "params": ["Assets/clip.wav", {"rpcType": 1, "value": "AudioClip"}],
    })
    ws = FakeWebSocket([frame])
    bridge = FakeBridge({"AssetDatabase.LoadAssetAtPath": None})
    await _handle_client(ws, bridge, _resolver(), r)
    response = decode_frame(ws.sent[0])
    assert response["id"] == 10


# ── RpcNameResolver.try_resolve ───────────────────────────────────────────────

def test_try_resolve_hit():
    r = _resolver_with_entries({("A", "B"): "X.A.B"})
    r.add_abbreviated_classes("A")
    assert r.try_resolve("B") == "X.A.B"


def test_try_resolve_miss():
    r = _resolver_with_entries({("A", "B"): "X.A.B"})
    r.add_abbreviated_classes("A")
    assert r.try_resolve("NoSuch") is None


def test_try_resolve_no_lookup():
    assert _resolver().try_resolve("B") is None


# ── RpcNameResolver.resolve_chain_params ──────────────────────────────────────

def test_resolve_chain_params_resolves_first_step():
    r = _resolver_with_entries({("A", "B"): "X.A.B"})
    r.add_abbreviated_classes("A")
    params = [{"obj": None, "steps": [{"name": "B"}, {"name": "C"}], "method": "D", "args": [1, 2]}]
    result = r.resolve_chain_params(params)
    assert result == [{"obj": None, "steps": [{"name": "X.A.B"}, {"name": "C"}], "method": "D", "args": [1, 2]}]


def test_resolve_chain_params_obj_not_null():
    r = _resolver_with_entries({("A", "B"): "X.A.B"})
    r.add_abbreviated_classes("A")
    params = [{"obj": {"rpcType": "T"}, "steps": [{"name": "B"}], "method": "D", "args": []}]
    assert r.resolve_chain_params(params) == params


def test_resolve_chain_params_unregistered_first_step():
    r = _resolver_with_entries({("A", "B"): "X.A.B"})
    r.add_abbreviated_classes("A")
    params = [{"obj": None, "steps": [{"name": "Other"}], "method": "D", "args": []}]
    assert r.resolve_chain_params(params) == params


def test_resolve_chain_params_empty_steps():
    r = _resolver_with_entries({("A", "B"): "X.A.B"})
    r.add_abbreviated_classes("A")
    params = [{"obj": None, "steps": [], "method": "D", "args": []}]
    assert r.resolve_chain_params(params) == params


def test_resolve_chain_params_does_not_mutate_original():
    r = _resolver_with_entries({("A", "B"): "X.A.B"})
    r.add_abbreviated_classes("A")
    params = [{"obj": None, "steps": [{"name": "B"}], "method": "D", "args": []}]
    r.resolve_chain_params(params)
    assert params == [{"obj": None, "steps": [{"name": "B"}], "method": "D", "args": []}]


# ── _handle_client chain first-step resolution ────────────────────────────────

@pytest.mark.asyncio
async def test_handle_client_resolves_chain_first_step():
    """JsonRpc_ResolveChain first step should be resolved before forwarding to C#."""
    r = _resolver_with_entries({("A", "B"): "X.A.B"})
    r.add_abbreviated_classes("A")
    frame = encode_frame({
        "jsonrpc": "2.0", "id": 11, "method": "JsonRpc_ResolveChain",
        "params": [{"obj": None, "steps": [{"name": "B"}, {"name": "C"}], "method": "D", "args": [1]}],
    })
    ws = FakeWebSocket([frame])
    bridge = FakeBridge({"JsonRpc_ResolveChain": None})
    await _handle_client(ws, bridge, r)
    method, params = bridge.calls[0]
    assert method == "JsonRpc_ResolveChain"
    assert params[0]["steps"][0]["name"] == "X.A.B"
    assert params[0]["steps"][1]["name"] == "C"


@pytest.mark.asyncio
async def test_handle_client_resolves_chainset_first_step():
    """JsonRpc_ResolveChainSet first step should be resolved before forwarding to C#."""
    r = _resolver_with_entries({("A", "B"): "X.A.B"})
    r.add_abbreviated_classes("A")
    frame = encode_frame({
        "jsonrpc": "2.0", "id": 12, "method": "JsonRpc_ResolveChainSet",
        "params": [{"obj": None, "steps": [{"name": "B"}], "property": "P", "value": 5}],
    })
    ws = FakeWebSocket([frame])
    bridge = FakeBridge({"JsonRpc_ResolveChainSet": None})
    await _handle_client(ws, bridge, r)
    method, params = bridge.calls[0]
    assert method == "JsonRpc_ResolveChainSet"
    assert params[0]["steps"][0]["name"] == "X.A.B"


@pytest.mark.asyncio
async def test_handle_client_chain_obj_not_null_unchanged():
    """A chain rooted on a concrete object keeps its first step unchanged."""
    r = _resolver_with_entries({("A", "B"): "X.A.B"})
    r.add_abbreviated_classes("A")
    frame = encode_frame({
        "jsonrpc": "2.0", "id": 13, "method": "JsonRpc_ResolveChain",
        "params": [{"obj": {"rpcType": "T"}, "steps": [{"name": "B"}], "method": "D", "args": []}],
    })
    ws = FakeWebSocket([frame])
    bridge = FakeBridge({"JsonRpc_ResolveChain": None})
    await _handle_client(ws, bridge, r)
    method, params = bridge.calls[0]
    assert params[0]["steps"][0]["name"] == "B"


# ── RpcNameResolver.try_resolve_class_method ──────────────────────────────────

def test_try_resolve_class_method_hit():
    r = _resolver_with_entries({("GameObject", "Find"): "UnityEngine.GameObject.Find"})
    assert r.try_resolve_class_method("GameObject", "Find") == "UnityEngine.GameObject.Find"


def test_try_resolve_class_method_miss():
    r = _resolver_with_entries({("GameObject", "Find"): "UnityEngine.GameObject.Find"})
    assert r.try_resolve_class_method("GameObject", "NoSuch") is None


def test_try_resolve_class_method_no_lookup():
    assert _resolver().try_resolve_class_method("GameObject", "Find") is None


# ── RpcNameResolver.try_collapse_static_chain ─────────────────────────────────

def test_collapse_static_chain_one_step():
    r = _resolver_with_entries({("GameObject", "Find"): "UnityEngine.GameObject.Find"})
    result = r.try_collapse_static_chain(
        "JsonRpc_ResolveChain",
        [{"obj": None, "steps": [{"name": "GameObject"}], "method": "Find", "args": ["UniLiquidLinkTestObject"]}],
    )
    assert result == ("UnityEngine.GameObject.Find", ["UniLiquidLinkTestObject"])


def test_collapse_static_chain_deep():
    r = _resolver_with_entries({("C", "D"): "A.B.C.D"})
    result = r.try_collapse_static_chain(
        "JsonRpc_ResolveChain",
        [{"obj": None, "steps": [{"name": "B"}, {"name": "C"}], "method": "D", "args": [42]}],
    )
    assert result == ("A.B.C.D", [42])


def test_collapse_static_chain_obj_not_null():
    r = _resolver_with_entries({("GameObject", "Find"): "UnityEngine.GameObject.Find"})
    result = r.try_collapse_static_chain(
        "JsonRpc_ResolveChain",
        [{"obj": {"rpcType": "T"}, "steps": [{"name": "GameObject"}], "method": "Find", "args": []}],
    )
    assert result is None


def test_collapse_static_chain_unregistered():
    r = _resolver_with_entries({("GameObject", "Find"): "UnityEngine.GameObject.Find"})
    result = r.try_collapse_static_chain(
        "JsonRpc_ResolveChain",
        [{"obj": None, "steps": [{"name": "Other"}], "method": "Find", "args": []}],
    )
    assert result is None


def test_collapse_static_chain_skips_direct_method():
    r = _resolver_with_entries({("AssetDatabase", "LoadAssetAtPath"): "_UnityEditor.AssetDatabase.LoadAssetAtPath"})
    result = r.try_collapse_static_chain(
        "JsonRpc_ResolveChain",
        [{"obj": None, "steps": [{"name": "AssetDatabase"}], "method": "LoadAssetAtPath", "args": []}],
    )
    assert result is None


def test_collapse_static_chain_empty_steps():
    r = _resolver_with_entries({("GameObject", "Find"): "UnityEngine.GameObject.Find"})
    result = r.try_collapse_static_chain(
        "JsonRpc_ResolveChain",
        [{"obj": None, "steps": [], "method": "Find", "args": []}],
    )
    assert result is None


def test_collapse_static_chain_not_call_form():
    r = _resolver_with_entries({("GameObject", "Find"): "UnityEngine.GameObject.Find"})
    result = r.try_collapse_static_chain(
        "JsonRpc_ResolveChainSet",
        [{"obj": None, "steps": [{"name": "GameObject"}], "property": "Find", "value": []}],
    )
    assert result is None


# ── _handle_client collapses static class-step chain ─────────────────────────

@pytest.mark.asyncio
async def test_handle_client_collapses_static_chain():
    """JsonRpc_ResolveChain with a class-step root should collapse to a direct call."""
    r = _resolver_with_entries({("GameObject", "Find"): "UnityEngine.GameObject.Find"})
    frame = encode_frame({
        "jsonrpc": "2.0", "id": 20, "method": "JsonRpc_ResolveChain",
        "params": [{"obj": None, "steps": [{"name": "GameObject"}], "method": "Find", "args": ["TestObject"]}],
    })
    ws = FakeWebSocket([frame])
    bridge = FakeBridge({"UnityEngine.GameObject.Find": None})
    await _handle_client(ws, bridge, r)
    method, params = bridge.calls[0]
    assert method == "UnityEngine.GameObject.Find"
    assert params == ["TestObject"]
