"""Unit tests for lliquidlink.core.RpcTimingStats."""
from lliquidlink.core import RpcTimingStats


def _zero_dict(**overrides):
    d = {
        "rpc_count": 0, "local_processing_time": 0.0, "communication_time": 0.0,
        "remote_time": 0.0, "total_time": 0.0,
        "response_network_time": 0.0, "request_network_time": 0.0,
        "bytes_sent": 0, "bytes_received": 0,
    }
    d.update(overrides)
    return d


def test_disabled_by_default_record_is_noop():
    stats = RpcTimingStats()
    stats.record(local_processing_time=1.0, remote_time=1.0)
    assert stats.to_dict() == _zero_dict()


def test_enabled_record_accumulates():
    stats = RpcTimingStats(enabled=True)
    stats.record(local_processing_time=1.0, remote_time=0.5)
    stats.record(local_processing_time=2.0, remote_time=0.5)
    d = stats.to_dict()
    assert d["rpc_count"] == 2
    assert d["local_processing_time"] == 3.0
    assert d["remote_time"] == 1.0
    assert d["total_time"] == 4.0


def test_reset_clears_totals_but_keeps_enabled():
    stats = RpcTimingStats(enabled=True)
    stats.record(local_processing_time=1.0, remote_time=1.0)
    stats.reset()
    assert stats.enabled is True
    assert stats.to_dict() == _zero_dict()


def test_toggle_enabled_after_construction():
    stats = RpcTimingStats()
    stats.record(local_processing_time=1.0, remote_time=1.0)
    assert stats.to_dict()["rpc_count"] == 0

    stats.enabled = True
    stats.record(local_processing_time=1.0, remote_time=1.0)
    assert stats.to_dict()["rpc_count"] == 1


def test_communication_time_is_derived_from_network_time_fields():
    stats = RpcTimingStats(enabled=True)
    stats.record(local_processing_time=1.0, remote_time=0.5,
                 response_network_time=0.2, request_network_time=0.1)
    stats.record(local_processing_time=1.0, remote_time=0.5,
                 response_network_time=0.3, request_network_time=0.4)
    d = stats.to_dict()
    assert d["response_network_time"] == 0.5
    assert d["request_network_time"] == 0.5
    assert d["communication_time"] == d["response_network_time"] + d["request_network_time"]


def test_record_network_time_defaults_to_zero_when_not_given():
    stats = RpcTimingStats(enabled=True)
    stats.record(local_processing_time=1.0, remote_time=1.0)
    d = stats.to_dict()
    assert d["response_network_time"] == 0.0
    assert d["request_network_time"] == 0.0
    assert d["communication_time"] == 0.0


def test_total_time_equals_local_plus_communication_plus_remote():
    stats = RpcTimingStats(enabled=True)
    stats.record(local_processing_time=1.0, remote_time=2.0,
                 response_network_time=0.3, request_network_time=0.4)
    d = stats.to_dict()
    assert d["total_time"] == d["local_processing_time"] + d["communication_time"] + d["remote_time"]


def test_record_bytes_disabled_by_default_is_noop():
    stats = RpcTimingStats()
    stats.record_bytes(sent=100, received=200)
    d = stats.to_dict()
    assert d["bytes_sent"] == 0
    assert d["bytes_received"] == 0


def test_record_bytes_enabled_accumulates_sent_and_received_independently():
    stats = RpcTimingStats(enabled=True)
    stats.record_bytes(sent=100)
    stats.record_bytes(received=50)
    stats.record_bytes(sent=10, received=20)
    d = stats.to_dict()
    assert d["bytes_sent"] == 110
    assert d["bytes_received"] == 70


def test_record_bytes_does_not_affect_rpc_count():
    stats = RpcTimingStats(enabled=True)
    stats.record_bytes(sent=100, received=100)
    stats.record_bytes(sent=100, received=100)
    assert stats.to_dict()["rpc_count"] == 0


def test_reset_clears_byte_and_network_time_totals():
    stats = RpcTimingStats(enabled=True)
    stats.record(local_processing_time=1.0, remote_time=1.0,
                 response_network_time=0.2, request_network_time=0.3)
    stats.record_bytes(sent=100, received=200)
    stats.reset()
    assert stats.enabled is True
    assert stats.to_dict() == _zero_dict()
