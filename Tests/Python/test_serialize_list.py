import json

from lliquidlink.client.models import RpcChainStep
from lliquidlink.client._serialization import Serialization


def _ser(x):
    """Round-trip through json using the encode_default hook."""
    return json.loads(json.dumps(x, default=Serialization.encode_default))


def test_plain_value():
    assert _ser([1, "x", None]) == [1, "x", None]


def test_dataclass():
    assert _ser([RpcChainStep("x")]) == [{"name": "x"}]


def test_list_of_plain():
    assert _ser([[1, 2]]) == [[1, 2]]


def test_list_of_dataclass():
    assert _ser([[RpcChainStep("x")]]) == [[{"name": "x"}]]


def test_dict_of_plain():
    assert _ser([{"k": "v"}]) == [{"k": "v"}]


def test_dict_value_dataclass():
    assert _ser([{"k": RpcChainStep("x")}]) == [{"k": {"name": "x"}}]


def test_nested_list():
    assert _ser([[[RpcChainStep("x")]]]) == [[[{"name": "x"}]]]


def test_dict_value_list():
    assert _ser([{"k": [RpcChainStep("x")]}]) == [{"k": [{"name": "x"}]}]


def test_list_value_dict():
    assert _ser([[{"k": RpcChainStep("x")}]]) == [[{"k": {"name": "x"}}]]


def test_object_hook_roundtrip():
    """InstanceObject dicts become proxies; plain dicts pass through."""
    sentinel = object()
    hook = Serialization(lambda data: sentinel).object_hook
    descriptor = {"instanceId": 1, "instanceObjectAttr" : 1}
    text = json.dumps({"obj": descriptor, "plain": {"k": "v"}})
    result = json.loads(text, object_hook=hook)
    assert result["obj"] is sentinel
    assert result["plain"] == {"k": "v"}
