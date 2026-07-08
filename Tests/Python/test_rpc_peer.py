"""Unit tests for the shared JsonRpcPeer core over an in-memory byte stream."""
import anyio
import pytest

from lliquidlink.core import (
    JsonRpcPeer, ConnectionClosedError, RpcError, encode_frame, decode_frame)


class LoopbackStream:
    """In-memory byte stream that answers each request frame via a responder."""

    def __init__(self, responder):
        self._responder = responder
        self._buffer = bytearray()
        self._data = anyio.Event()
        self._closed = False

    async def send(self, data: bytes) -> None:
        reply = self._responder(decode_frame(data))
        if reply is not None:
            self._buffer.extend(encode_frame(reply))
            self._data.set()

    async def receive(self, max_bytes: int = 65536) -> bytes:
        while not self._buffer:
            if self._closed:
                raise anyio.EndOfStream
            await self._data.wait()
            self._data = anyio.Event()
        chunk = bytes(self._buffer[:max_bytes])
        del self._buffer[:max_bytes]
        return chunk

    async def aclose(self) -> None:
        self._closed = True
        self._data.set()


def _echo(msg):
    """Reply to a request with its first param as the result; drop notifications."""
    if msg.get("id") is None:
        return None
    return {"jsonrpc": "2.0", "id": msg["id"], "result": msg["params"][0]}


@pytest.mark.anyio
async def test_request_roundtrip():
    peer = JsonRpcPeer(LoopbackStream(_echo))
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        assert await peer.request("Echo", [42]) == 42
        assert await peer.request("Echo", ["hello"]) == "hello"
        await peer.aclose()
        tg.cancel_scope.cancel()


@pytest.mark.anyio
async def test_error_response_raises():
    def responder(msg):
        return {"jsonrpc": "2.0", "id": msg["id"],
                "error": {"code": -32603, "message": "boom"}}

    peer = JsonRpcPeer(LoopbackStream(responder))
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        with pytest.raises(RpcError, match="boom"):
            await peer.request("Fail", [])
        await peer.aclose()
        tg.cancel_scope.cancel()


@pytest.mark.anyio
async def test_closed_peer_rejects():
    peer = JsonRpcPeer(LoopbackStream(_echo))
    await peer.aclose()
    with pytest.raises(ConnectionClosedError):
        await peer.request("Echo", [1])


@pytest.fixture
def anyio_backend():
    return "asyncio"
