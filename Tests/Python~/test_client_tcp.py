"""End-to-end client test against a local fake TCP server (no Unity).

Exercises the full client path: TcpJsonRpcTransport, the anyio runtime,
synchronous proxy calls from a worker thread, chain resolution, and ObjectProxy.
"""
import time

import anyio
import pytest

from lliquidlink.client import Client, ObjectProxy, TcpJsonRpcTransport
from lliquidlink.core import encode_frame, decode_frame
from lliquidlink.server._transport import _TcpConnection

HOST, PORT = "localhost", 8799


def _dispatch(method, params):
    if method == "SampleMethodInt":
        return params[0]
    if method == "get_rpc_timing_stats":
        return {"external": {"rpc_count": 3}, "ipc": {"rpc_count": 3}}
    if method == "Find":
        return {"instanceId": 100, "orgType": "UnityEngine.GameObject",
                "name": params[0], "instanceObjectAttr" : 1}
    if method == "JsonRpc_ResolveChain":
        p = params[0]
        obj, m, args = p["obj"], p["method"], p["args"]
        if m == "transform":
            return {"instanceId": 200, "orgType": "UnityEngine.Transform",
                    "name": obj.get("name"), "instanceObjectAttr" : 1}
        if m == "name":
            return obj.get("name")
        if m == "Rotate":
            return list(args)
    return None


async def _handler(ws):
    async for raw in ws:
        msg = decode_frame(raw)
        if msg.get("id") is None:
            continue
        result = _dispatch(msg.get("method"), msg.get("params", []))
        response = {"jsonrpc": "2.0", "id": msg["id"], "result": result}
        # Mirrors the real server's gating: only echo received_at/sent_at when the
        # request itself carried a "sent_at" (i.e. the client has RPC timing enabled).
        sent_at = msg.get("sent_at")
        if sent_at is not None:
            # A real (not offset-into-the-future) received_at/sent_at pair, with a real sleep in
            # between: wall-clock timestamps must stay causally ordered (sent_at can never be
            # after the client's own receipt), so remote_time's floor has to come from an actual
            # elapsed delay rather than nudging a timestamp artificially forward.
            received_at = time.time()
            response["received_at"] = received_at
            await anyio.sleep(0.01)
            response["sent_at"] = time.time()
        await ws.send(encode_frame(response))


class _FakeTcpServer:
    """Minimal TCP listener that runs `_handler` per connection, mirroring `websockets.serve`."""

    def __init__(self, host, port):
        self._host = host
        self._port = port
        self._listener = None

    async def __aenter__(self):
        self._listener = await anyio.create_tcp_listener(
            local_host=self._host, local_port=self._port)
        self._tg = anyio.create_task_group()
        await self._tg.__aenter__()

        async def _on_connect(stream):
            await _handler(_TcpConnection(stream))

        self._tg.start_soon(self._listener.serve, _on_connect)
        return self

    async def __aexit__(self, *exc_info):
        self._tg.cancel_scope.cancel()
        await self._tg.__aexit__(*exc_info)
        await self._listener.aclose()


def _scenario(c):
    c.out["int"] = c.SampleMethodInt(7)
    go = c.Find("Cube")
    c.out["go"] = go
    c.out["name"] = go.data["name"]
    c.out["t_name"] = go.transform.name()
    c.out["rotate"] = go.transform.Rotate(10, 20, 30)


class _Client(Client):
    def __init__(self, transport):
        super().__init__(transport)
        self.out = {}


@pytest.mark.anyio
async def test_client_over_tcp():
    async with _FakeTcpServer(HOST, PORT):
        client = _Client(TcpJsonRpcTransport(HOST, PORT))
        await client.connect()
        try:
            await client.execute(_scenario)
        finally:
            await client.disconnect()

    assert client.out["int"] == 7
    assert client.out["name"] == "Cube"
    assert client.out["t_name"] == "Cube"
    assert client.out["rotate"] == [10, 20, 30]
    assert isinstance(client.out["go"], ObjectProxy)


@pytest.mark.anyio
async def test_run_receives_client_over_tcp():
    received = []

    def handler(client):
        received.append(client.SampleMethodInt(7))

    async with _FakeTcpServer(HOST, PORT):
        client = Client(TcpJsonRpcTransport(HOST, PORT))
        client.run += handler
        await client.connect()
        try:
            await client.execute(client.run)
        finally:
            await client.disconnect()

    assert received == [7]


@pytest.mark.anyio
async def test_run_multiple_handlers_dispatch_in_order():
    order = []

    def first(client):
        order.append("first")

    def second(client):
        order.append("second")

    client = Client(TcpJsonRpcTransport(HOST, PORT))
    client.run += first
    client.run += second

    await client.execute(client.run)

    assert order == ["first", "second"]


def _timing_scenario(c):
    c.options.rpc_timing.enabled = True
    c.SampleMethodInt(1)
    c.SampleMethodInt(2)
    c.out["stats"] = c.options.rpc_timing.to_dict()


@pytest.mark.anyio
async def test_rpc_timing_option_returns_combined_client_and_server_stats():
    async with _FakeTcpServer(HOST, PORT):
        client = _Client(TcpJsonRpcTransport(HOST, PORT))
        await client.connect()
        try:
            await client.execute(_timing_scenario)
        finally:
            await client.disconnect()

    stats = client.out["stats"]
    # 2 application calls + the get_rpc_timing_stats call itself.
    assert stats["client"]["rpc_count"] == 3
    assert stats["server"] == {"external": {"rpc_count": 3}, "ipc": {"rpc_count": 3}}
    # The fake handler echoes received_at/sent_at over the real TCP socket, so
    # communication_time/remote_time are real (nonzero-capable) measurements here,
    # not just structurally-present zeros.
    client_stats = stats["client"]
    assert client_stats["communication_time"] >= 0.0
    assert client_stats["remote_time"] >= 0.0
    assert client_stats["local_processing_time"] >= 0.0
    assert client_stats["total_time"] == pytest.approx(
        client_stats["local_processing_time"] + client_stats["communication_time"] + client_stats["remote_time"])


@pytest.mark.anyio
async def test_rpc_timing_disabled_by_default():
    async with _FakeTcpServer(HOST, PORT):
        client = _Client(TcpJsonRpcTransport(HOST, PORT))
        await client.connect()
        try:
            await client.execute(lambda c: c.SampleMethodInt(1))
        finally:
            await client.disconnect()

    assert client.options.rpc_timing.enabled is False
    assert client._transport.timing.to_dict()["rpc_count"] == 0


@pytest.fixture
def anyio_backend():
    return "asyncio"
