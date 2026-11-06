"""Client-side feature options, exposed as `Client.options`."""
from __future__ import annotations
from typing import Any, Dict, TYPE_CHECKING
if TYPE_CHECKING:
    from ._interfaces import Transport


class RpcTimingOption:
    """RPC timing feature: enable/disable and combined stats retrieval in one class.

    `enabled` toggles this connection's client-side measurement. `to_dict()`
    returns both this connection's client-side stats and the server's own
    stats (fetched via a round-trip RPC) as one combined dict.
    """

    def __init__(self, transport: Transport):
        self._transport: Transport = transport

    @property
    def enabled(self) -> bool:
        return self._transport.timing.enabled

    @enabled.setter
    def enabled(self, value: bool) -> None:
        self._transport.timing.enabled = value

    def to_dict(self) -> Dict[str, Any]:
        """Return client-side stats plus the server's stats for this connection.

        Each side's stats dict is `RpcTimingStats.to_dict()`'s shape: a
        `rpc_count`, the `local_processing_time`/`communication_time`/
        `remote_time` breakdown of `total_time` (see `core._timing` for the
        model), the `request_network_time`/`response_network_time` pair
        `communication_time` is derived from, and wire byte counts. `server`
        additionally nests `external`/`ipc`/`csharp` legs, each in this same
        shape.

        Note: fetching the server's stats is itself one RPC call, so when
        `enabled` is True it adds 1 to the returned client-side rpc_count.
        """
        server_stats = self._transport.call_sync("get_rpc_timing_stats", [])
        return {"client": self._transport.timing.to_dict(), "server": server_stats}


class ClientOptions:
    """Container for optional Client features; add new feature toggles here."""

    def __init__(self, transport: Transport):
        self.rpc_timing: RpcTimingOption = RpcTimingOption(transport)
