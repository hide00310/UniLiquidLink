"""Unit tests for the shared JsonRpcPeer core over an in-memory byte stream."""
import time

import anyio
import pytest

from lliquidlink.core import (
    JsonRpcPeer, ConnectionClosedError, RpcError, RpcTimingStats, encode_frame, decode_frame)


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


def _echo_with_sent_at(msg):
    """Like `_echo`, but also echoes back a fresh `sent_at`, modeling a peer
    (e.g. the gateway) that itself has RPC timing enabled but predates the
    `received_at` echo (only `response_network_time` can be derived)."""
    if msg.get("id") is None:
        return None
    return {"jsonrpc": "2.0", "id": msg["id"], "result": msg["params"][0], "sent_at": time.time()}


def _echo_with_received_at(msg):
    """Like `_echo_with_sent_at`, but also echoes back `received_at`, modeling a
    peer that follows the full received_at/sent_at echo protocol (e.g. the C#
    side after this change): request_network_time and remote_time become
    derivable in addition to response_network_time. Offsets are fixed rather
    than measured so the assertions are not flaky under clock resolution.
    """
    if msg.get("id") is None:
        return None
    received_at = time.time() + 0.05  # request_network_time ~= +0.05s
    sent_at = received_at + 0.05      # remote_time ~= 0.05s
    return {"jsonrpc": "2.0", "id": msg["id"], "result": msg["params"][0],
            "received_at": received_at, "sent_at": sent_at}


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
        with pytest.raises(RpcError, match="boom") as excinfo:
            await peer.request("Fail", [1, "a"])
        # The failing call is attached without changing the exception type or message.
        assert str(excinfo.value) == "boom"
        assert excinfo.value.method == "Fail"
        assert excinfo.value.params == [1, "a"]
        await peer.aclose()
        tg.cancel_scope.cancel()


@pytest.mark.anyio
async def test_closed_peer_rejects():
    peer = JsonRpcPeer(LoopbackStream(_echo))
    await peer.aclose()
    with pytest.raises(ConnectionClosedError):
        await peer.request("Echo", [1])


@pytest.mark.anyio
async def test_request_records_timing_when_enabled():
    timing = RpcTimingStats(enabled=True)
    peer = JsonRpcPeer(LoopbackStream(_echo), timing=timing)
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        await peer.request("Echo", [1])
        await peer.request("Echo", [2])
        await peer.aclose()
        tg.cancel_scope.cancel()

    stats = timing.to_dict()
    assert stats["rpc_count"] == 2
    assert stats["communication_time"] >= 0.0
    assert stats["local_processing_time"] >= 0.0
    assert stats["total_time"] == stats["local_processing_time"] + stats["communication_time"] + stats["remote_time"]


@pytest.mark.anyio
async def test_request_does_not_record_timing_by_default():
    peer = JsonRpcPeer(LoopbackStream(_echo))
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        await peer.request("Echo", [1])
        await peer.aclose()
        tg.cancel_scope.cancel()

    assert peer.timing.to_dict()["rpc_count"] == 0


@pytest.mark.anyio
async def test_request_records_response_network_time_when_reply_echoes_sent_at():
    timing = RpcTimingStats(enabled=True)
    peer = JsonRpcPeer(LoopbackStream(_echo_with_sent_at), timing=timing)
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        await peer.request("Echo", [1])
        await peer.aclose()
        tg.cancel_scope.cancel()

    stats = timing.to_dict()
    assert stats["response_network_time"] >= 0.0
    # response_network_time is a sub-component of communication_time, never larger.
    assert stats["response_network_time"] <= stats["communication_time"]
    # This fake echoes sent_at but not received_at, so request_network_time/remote_time
    # (which both require received_at) must stay 0.0 -- the backward-compatible case.
    assert stats["request_network_time"] == 0.0
    assert stats["remote_time"] == 0.0


@pytest.mark.anyio
async def test_request_response_network_time_zero_when_reply_has_no_sent_at():
    """Models the ipc leg: C# never echoes back a sent_at."""
    timing = RpcTimingStats(enabled=True)
    peer = JsonRpcPeer(LoopbackStream(_echo), timing=timing)
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        await peer.request("Echo", [1])
        await peer.aclose()
        tg.cancel_scope.cancel()

    assert timing.to_dict()["response_network_time"] == 0.0


@pytest.mark.anyio
async def test_request_records_request_network_time_and_remote_time_when_reply_echoes_received_at():
    timing = RpcTimingStats(enabled=True)
    peer = JsonRpcPeer(LoopbackStream(_echo_with_received_at), timing=timing)
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        await peer.request("Echo", [1])
        await peer.aclose()
        tg.cancel_scope.cancel()

    stats = timing.to_dict()
    assert stats["request_network_time"] > 0.0
    assert stats["remote_time"] > 0.0
    assert stats["remote_time"] == pytest.approx(0.05, abs=0.03)


@pytest.mark.anyio
async def test_bytes_sent_and_received_accumulate_on_request():
    timing = RpcTimingStats(enabled=True)
    peer = JsonRpcPeer(LoopbackStream(_echo_with_sent_at), timing=timing)
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        await peer.request("Echo", [1])
        await peer.aclose()
        tg.cancel_scope.cancel()

    stats = timing.to_dict()
    assert stats["bytes_sent"] > 0
    assert stats["bytes_received"] > 0


@pytest.mark.anyio
async def test_notify_records_bytes_sent_when_enabled():
    timing = RpcTimingStats(enabled=True)
    peer = JsonRpcPeer(LoopbackStream(_echo), timing=timing)
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        await peer.notify("Fire", [1])
        await peer.aclose()
        tg.cancel_scope.cancel()

    stats = timing.to_dict()
    assert stats["rpc_count"] == 0
    assert stats["bytes_sent"] > 0


@pytest.mark.anyio
async def test_notify_records_no_bytes_when_disabled():
    peer = JsonRpcPeer(LoopbackStream(_echo))
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        await peer.notify("Fire", [1])
        await peer.aclose()
        tg.cancel_scope.cancel()

    assert peer.timing.to_dict()["bytes_sent"] == 0


@pytest.mark.anyio
async def test_concurrent_requests_attribute_network_time_to_correct_slot():
    """Regression test for the pipelining-correlation risk: with two requests in
    flight at once, each one's response_network_time/remote_time must come from
    ITS OWN response's embedded sent_at/received_at, never from a peer-level
    shared variable that could be overwritten by the other in-flight request's
    response.
    """
    def responder(msg):
        if msg.get("id") is None:
            return None
        is_slow = msg["id"] == 1
        # id 1's reply claims to have been sent ~10s ago (large response_network_time)
        # and to have taken ~5s of remote processing (large remote_time); id 2's reply
        # claims both are ~0. If the two were cross-attributed or shared a single
        # variable, the accumulated totals would land near 0s/20s and 0s/10s instead
        # of ~10s and ~5s.
        sent_at = time.time() - (10.0 if is_slow else 0.0)
        received_at = sent_at - (5.0 if is_slow else 0.0)
        return {"jsonrpc": "2.0", "id": msg["id"], "result": msg["params"][0],
                "sent_at": sent_at, "received_at": received_at}

    timing = RpcTimingStats(enabled=True)
    peer = JsonRpcPeer(LoopbackStream(responder), timing=timing)
    async with anyio.create_task_group() as tg:
        tg.start_soon(peer.serve)
        async with anyio.create_task_group() as callers:
            callers.start_soon(peer.request, "Echo", ["a"])
            callers.start_soon(peer.request, "Echo", ["b"])
        await peer.aclose()
        tg.cancel_scope.cancel()

    stats = timing.to_dict()
    assert stats["rpc_count"] == 2
    assert 9.0 <= stats["response_network_time"] <= 11.0
    assert 4.0 <= stats["remote_time"] <= 6.0


@pytest.fixture
def anyio_backend():
    return "asyncio"
