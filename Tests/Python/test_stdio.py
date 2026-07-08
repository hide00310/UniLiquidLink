"""Round-trip test for StdioByteStream + JsonRpcPeer over OS pipes (no Unity)."""
import json
import os
import struct

import anyio
import pytest
from anyio.streams.buffered import BufferedByteReceiveStream

from lliquidlink.core import JsonRpcPeer, StdioByteStream, encode_frame

_LEN = struct.Struct(">I")


async def _echo_server(stream):
    """Read 4-byte framed requests and reply with the first param as result."""
    reader = BufferedByteReceiveStream(stream)
    while True:
        try:
            length = _LEN.unpack(await reader.receive_exactly(4))[0]
            msg = json.loads(await reader.receive_exactly(length))
        except (anyio.EndOfStream, anyio.IncompleteRead, anyio.ClosedResourceError):
            break
        if msg.get("id") is not None:
            await stream.send(encode_frame(
                {"jsonrpc": "2.0", "id": msg["id"], "result": msg["params"][0]}))


@pytest.mark.anyio
async def test_stdio_roundtrip():
    c2s_r, c2s_w = os.pipe()
    s2c_r, s2c_w = os.pipe()
    client_stream = StdioByteStream(os.fdopen(s2c_r, "rb"), os.fdopen(c2s_w, "wb"))
    server_stream = StdioByteStream(os.fdopen(c2s_r, "rb"), os.fdopen(s2c_w, "wb"))
    client = JsonRpcPeer(client_stream)

    async with anyio.create_task_group() as tg:
        tg.start_soon(_echo_server, server_stream)
        tg.start_soon(client.serve)
        with anyio.fail_after(5):
            assert await client.request("Echo", [99]) == 99
            assert await client.request("Echo", ["x"]) == "x"
        await client.aclose()
        tg.cancel_scope.cancel()


@pytest.fixture
def anyio_backend():
    return "asyncio"
