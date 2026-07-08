"""End-to-end client test against a local fake TCP server (no Unity).

Exercises the full client path: TcpJsonRpcTransport, the anyio runtime,
synchronous proxy calls from a worker thread, chain resolution, and ObjectProxy.
"""
import anyio
import pytest

from lliquidlink.client import Client, ObjectProxy, TcpJsonRpcTransport
from lliquidlink.core import encode_frame, decode_frame
from lliquidlink.server._transport import _TcpConnection

HOST, PORT = "localhost", 8799


def _dispatch(method, params):
    if method == "SampleMethodInt":
        return params[0]
    if method == "Find":
        return {"instanceId": 100, "orgType": "UnityEngine.GameObject",
                "name": params[0], "instanceObjectAttr" : 1}
    if method == "JsonRpc_ResolveChain":
        obj, _steps, m, args = params
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
        await ws.send(encode_frame({"jsonrpc": "2.0", "id": msg["id"], "result": result}))


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
async def test_on_execute_receives_client_over_tcp():
    received = []

    def handler(client):
        received.append(client.SampleMethodInt(7))

    async with _FakeTcpServer(HOST, PORT):
        client = Client(TcpJsonRpcTransport(HOST, PORT))
        client.on_execute += handler
        await client.connect()
        try:
            await client.execute(client.on_execute)
        finally:
            await client.disconnect()

    assert received == [7]


@pytest.mark.anyio
async def test_on_execute_multiple_handlers_dispatch_in_order():
    order = []

    def first(client):
        order.append("first")

    def second(client):
        order.append("second")

    client = Client(TcpJsonRpcTransport(HOST, PORT))
    client.on_execute += first
    client.on_execute += second

    await client.execute(client.on_execute)

    assert order == ["first", "second"]


@pytest.fixture
def anyio_backend():
    return "asyncio"
