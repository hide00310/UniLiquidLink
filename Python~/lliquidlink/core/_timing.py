"""Per-connection RPC timing: enable/disable and stats retrieval live in one class.

Shared by the client (`client._transports.StreamTransport`) and the server
(`server.server` and `server.ipc_bridge.IpcBridge`) so both legs of the bridge
can time their own "1 RPC" using the same accumulator shape.

One RPC's total time is split into three non-overlapping buckets:

    total_time = local_processing_time + communication_time + remote_time

    local_processing_time  time spent in this process (message building,
                            response decoding, ...)
    communication_time     pure wire transit time; NOT stored directly, it is
                            *derived* in `to_dict()` as
                            `request_network_time + response_network_time` so
                            "sum of the breakdown == communication_time" holds
                            by construction
    remote_time            time from the peer receiving the request to the
                            peer sending the response (0.0 for legs that are
                            pure responders, e.g. the C# side)

`local_processing_time` and `remote_time` are measured with
`time.perf_counter()` and are only meaningful within the process that
recorded them (comparable across calls on the same process, never across
processes). `response_network_time` and `request_network_time` are measured
with `time.time()` (wall clock) using `"sent_at"`/`"received_at"` timestamps
embedded in the wire messages, so they are individually skewed by whatever
clock offset delta exists between the two processes. That skew cancels out in
the two values this module treats as authoritative:

- `communication_time` (`request_network_time + response_network_time`): the
  `+delta` on the inbound leg and the `-delta` on the outbound leg cancel.
- `remote_time`: both of its timestamps (`received_at`, `sent_at`) are
  produced by the *same* peer process, so no cross-process clock is involved.

The individual `request_network_time`/`response_network_time` values are
still exposed for diagnostic purposes, but only `communication_time` (their
sum) should be trusted as a measurement.

`bytes_sent`/`bytes_received` count every frame that crosses the wire on this
connection, including fire-and-forget notifications, so they do not
correlate 1:1 with `rpc_count`.

A single `RpcTimingStats` instance is only ever touched from one event loop
(one asyncio task at a time awaits between mutations); it is not safe to
share across threads or event loops.
"""
from __future__ import annotations
from typing import Any, Dict


class RpcTimingStats:
    """Tracks one connection's RPC timing: total RPC count, accumulated local
    processing time, remote processing time, plus the wall-clock network
    transit times (from which `communication_time` is derived) and wire byte
    volume described in the module docstring.
    """

    def __init__(self, enabled: bool = False):
        self.enabled: bool = enabled
        self._rpc_count: int = 0
        self._local_processing_time: float = 0.0
        self._remote_time: float = 0.0
        self._response_network_time: float = 0.0
        self._request_network_time: float = 0.0
        self._bytes_sent: int = 0
        self._bytes_received: int = 0

    def record(
        self,
        local_processing_time: float,
        remote_time: float = 0.0,
        response_network_time: float = 0.0,
        request_network_time: float = 0.0,
    ) -> None:
        """Add one RPC's timing to the running totals; no-op when disabled."""
        if not self.enabled:
            return
        self._rpc_count += 1
        self._local_processing_time += local_processing_time
        self._remote_time += remote_time
        self._response_network_time += response_network_time
        self._request_network_time += request_network_time

    def record_bytes(self, sent: int = 0, received: int = 0) -> None:
        """Add wire byte counts to the running totals; no-op when disabled.

        Does not touch `rpc_count`: this is called for every frame on the
        wire (including notifications), not just completed request/response
        cycles.
        """
        if not self.enabled:
            return
        self._bytes_sent += sent
        self._bytes_received += received

    def reset(self) -> None:
        """Clear accumulated totals for a new connection; keeps `enabled`."""
        self._rpc_count = 0
        self._local_processing_time = 0.0
        self._remote_time = 0.0
        self._response_network_time = 0.0
        self._request_network_time = 0.0
        self._bytes_sent = 0
        self._bytes_received = 0

    def to_dict(self) -> Dict[str, Any]:
        """Return the current stats as a plain dict.

        `communication_time` is derived (`request_network_time +
        response_network_time`), not stored, so the breakdown always sums to
        it exactly.
        """
        communication_time = self._request_network_time + self._response_network_time
        return {
            "rpc_count": self._rpc_count,
            "local_processing_time": self._local_processing_time,
            "communication_time": communication_time,
            "remote_time": self._remote_time,
            "total_time": self._local_processing_time + communication_time + self._remote_time,
            "response_network_time": self._response_network_time,
            "request_network_time": self._request_network_time,
            "bytes_sent": self._bytes_sent,
            "bytes_received": self._bytes_received,
        }
