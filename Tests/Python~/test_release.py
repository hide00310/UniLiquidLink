import pytest
from unittest.mock import MagicMock
from lliquidlink.client import Client, gc_flush


class FakeClient(Client):
    """Client subclass whose flush_releases() call count is observable."""

    def __init__(self):
        transport = MagicMock()
        transport.closed = True
        super().__init__(transport)
        self.flush_count = 0

    def flush_releases(self):
        self.flush_count += 1


class ProxyLike:
    """Responds to any attribute access, like ObjectProxy/PropertyProxy, but is not a Client."""

    def __getattr__(self, name):
        return ProxyLike()


def test_flush_when_first_arg_is_client():
    client = FakeClient()

    @gc_flush
    def func(c):
        return "ok"

    assert func(client) == "ok"
    assert client.flush_count == 1


def test_flush_when_client_is_later_positional_arg():
    client = FakeClient()

    @gc_flush
    def method(self_, c, value):
        return value

    assert method(object(), client, 42) == 42
    assert client.flush_count == 1


def test_flush_happens_even_if_func_raises():
    client = FakeClient()

    @gc_flush
    def func(c):
        raise ValueError("boom")

    with pytest.raises(ValueError):
        func(client)
    assert client.flush_count == 1


def test_raises_type_error_without_client_arg():
    called = False

    @gc_flush
    def func(x, y):
        nonlocal called
        called = True
        return x + y

    with pytest.raises(TypeError):
        func(1, 2)
    assert called is False


def test_proxy_like_object_is_not_mistaken_for_client():
    client = FakeClient()
    proxy = ProxyLike()

    @gc_flush
    def method(self_, maybe_proxy, c):
        return "ok"

    assert method(object(), proxy, client) == "ok"
    assert client.flush_count == 1
