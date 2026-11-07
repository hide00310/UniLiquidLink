import gc

from lliquidlink.client._identity_cache import ObjectIdentityCache


class DummyProxy:
    """Minimal stand-in for ObjectProxy: just needs to be weakly referenceable."""

    def __init__(self, data):
        self.data = data


def test_same_instance_id_returns_same_proxy():
    cache = ObjectIdentityCache()
    data = {"instanceId": 1}

    first = cache.get_or_create(data, DummyProxy)
    second = cache.get_or_create(data, DummyProxy)

    assert first is second


def test_different_instance_ids_return_different_proxies():
    cache = ObjectIdentityCache()

    first = cache.get_or_create({"instanceId": 1}, DummyProxy)
    second = cache.get_or_create({"instanceId": 2}, DummyProxy)

    assert first is not second


def test_missing_instance_id_is_never_cached():
    cache = ObjectIdentityCache()
    calls = 0

    def factory(data):
        nonlocal calls
        calls += 1
        return DummyProxy(data)

    cache.get_or_create({}, factory)
    cache.get_or_create({}, factory)

    assert calls == 2


def test_collected_proxy_is_recreated_on_next_lookup():
    cache = ObjectIdentityCache()
    data = {"instanceId": 1}

    first = cache.get_or_create(data, DummyProxy)
    del first
    gc.collect()

    second = cache.get_or_create(data, DummyProxy)

    assert second is not None
