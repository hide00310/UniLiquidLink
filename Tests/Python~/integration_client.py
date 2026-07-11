"""
IntegrationClient and pytest fixture for integration tests.
Edit this file to add/remove test scenarios (run_* methods).
Run generate_test_integration.py to regenerate test_integration.py.
"""
import pytest
import pytest_asyncio
from lliquidlink.client import Client, ObjectProxy, TcpJsonRpcTransport
from lliquidlink.client.models import type_, enum


def _normalize(obj):
    """Exclude instanceId (changes each run) and return only stable values."""
    if isinstance(obj, ObjectProxy):
        return {"orgType": obj.data.get("orgType"), "name": obj.data.get("name")}
    if isinstance(obj, list):
        return [_normalize(item) for item in obj]
    if isinstance(obj, dict):
        return {k: _normalize(v) for k, v in obj.items()}
    return obj


class IntegrationClient(Client):
    def __init__(self):
        super().__init__(TcpJsonRpcTransport("localhost", 8700))
        self.captured = {}
        self.on_execute += self._on_execute

    def _try(self, fn):
        try:
            ret = _normalize(fn())
        except Exception as e:
            return f"{type(e).__name__}: {e}"
        return ret

    def run_call_unknown_method(self):
        return self._try(self.NoSuchMethod999)

    def run_find_test_object(self):
        return self._try(lambda: self.Find("UniLiquidLinkTestObject"))

    def run_find_test_game_object(self):
        return self._try(lambda: self.GameObject.Find("UniLiquidLinkTestObject"))

    def run_sample_method_int(self):
        return self._try(lambda: self.SampleMethodInt(123))

    def run_sample_method_int_str(self):
        return self._try(lambda: self.SampleMethodIntStr(123, "abc"))

    def run_sample_game_object(self):
        go = self.Find("UniLiquidLinkTestObject")
        return self._try(lambda: self.SampleGameObject(go))

    def run_sample_primitive_object_int(self):
        return self._try(lambda: self.SamplePrimitiveObject(42))

    def run_sample_primitive_object_string(self):
        return self._try(lambda: self.SamplePrimitiveObject("hello"))

    def run_sample_primitive_object_bool(self):
        return self._try(lambda: self.SamplePrimitiveObject(True))

    def run_sample_game_object_array(self):
        go = self.Find("UniLiquidLinkTestObject")
        return self._try(lambda: self.SampleGameObjectArray([go, go]))

    def run_sample_game_object_dict(self):
        go = self.Find("UniLiquidLinkTestObject")
        return self._try(lambda: self.SampleGameObjectDict({"a": go, "b": go}))

    def run_sample_game_object_list(self):
        go = self.Find("UniLiquidLinkTestObject")
        return self._try(lambda: self.SampleGameObjectList([go, go]))

    def run_sample_enum(self):
        return self._try(lambda: self.SampleEnum(enum("Beta")))

    def run_get_transform(self):
        go = self.Find("UniLiquidLinkTestObject")
        return self._try(lambda: go.transform())

    def run_rotate(self):
        go = self.Find("UniLiquidLinkTestObject")
        t = go.transform()
        return self._try(lambda: t.Rotate(10, 20, 30))

    def run_transform_rotate(self):
        go = self.Find("UniLiquidLinkTestObject")
        return self._try(lambda: go.transform.Rotate(10, 20, 30))

    def run_chained_game_object(self):
        go = self.Find("UniLiquidLinkTestObject")
        return self._try(lambda: go.transform.gameObject())

    def run_sample_vector3(self):
        return self._try(lambda: self.SampleVector3({"x": 1, "y": 2, "z": 3}))

    def run_sample_vector3_array(self):
        return self._try(lambda: self.SampleVector3Array(
            [{"x": 1, "y": 2, "z": 3}, {"x": 4, "y": 5, "z": 6}]))

    def run_sample_vector3_list(self):
        return self._try(lambda: self.SampleVector3List(
            [{"x": 1, "y": 2, "z": 3}, {"x": 4, "y": 5, "z": 6}]))

    def run_sample_vector3_dict(self):
        return self._try(lambda: self.SampleVector3Dict(
            {"a": {"x": 1, "y": 2, "z": 3}, "b": {"x": 4, "y": 5, "z": 6}}))

    def run_load_asset_with_type(self):
        self.add_abbreviated_namespaces(["UnityEngine"])
        return self._try(
            lambda: self.LoadAssetAtPath("Assets/UniLiquidLinkTest.mat", type_("Material")))

    def run_load_asset_database(self):
        return self._try(
            lambda: self.AssetDatabase.LoadAssetAtPath("Assets/UniLiquidLinkTest.mat", type_("Material")))

    def run_transform_position(self):
        go = self.Find("UniLiquidLinkTestObject")
        return self._try(lambda: go.transform.position())

    def run_set_transform_position(self):
        go = self.Find("UniLiquidLinkTestObject")

        def set_and_get():
            go.transform.position = {"x": 1, "y": 2, "z": 3}
            result = go.transform.position()
            # Restore shared state so this test does not affect others.
            go.transform.position = {"x": 0, "y": 0, "z": 0}
            return result

        return self._try(set_and_get)

    def run_object_properties(self):
        go = self.Find("UniLiquidLinkTestObject")
        result = {}
        result["name"] = self._try(go.name)
        result["active"] = self._try(go.activeSelf)
        result["hideFlags"] = self._try(go.hideFlags)
        return result

    def run_add_abbreviated_namespaces(self):
        """Verify add_abbreviated_namespaces enables short type name resolution."""
        self.add_abbreviated_namespaces(["UnityEngine"])
        return self._try(
            lambda: self.LoadAssetAtPath("Assets/UniLiquidLinkTest.mat", type_("Material")))

    def run_set_abbreviated_classes(self):
        """Verify end-to-end: add_abbreviated_classes enables abbreviated method calls."""
        self.add_abbreviated_classes(["UniLiquidLinkIntegrationTest"])
        return self._try(lambda: self.SampleMethodInt(42))

    def run_set_abbreviated_classes_find(self):
        """Verify add_abbreviated_classes works for GameObject.Find resolution."""
        self.add_abbreviated_classes(["GameObject"])
        return self._try(lambda: self.Find("UniLiquidLinkTestObject"))

    def run_nested_static_class(self):
        """Verify a class-step chain resolves a nested static method end-to-end."""
        return self._try(lambda: self.NestedMath.StaticAdd(2, 3))

    def run_object_overload(self):
        g = self.Find("UniLiquidLinkTestObject")
        t = g.transform()
        return self._try(lambda: {"GameObject" : self.SampleClass.ObjectOverload(g), "Transform" : self.SampleClass.ObjectOverload(t)})

    def run_object_method(self):
        g = self.Find("UniLiquidLinkTestObject")
        return self._try(lambda: self.SampleClass.ObjectMethod(g))

    def _on_execute(self, _client):
        self.add_abbreviated_classes(["GameObject", "UniLiquidLinkIntegrationTest", "AssetDatabase"])
        self.add_abbreviated_namespaces(["UnityEngine"])
        self.captured = {
            func.replace("run_", ""): getattr(self, func)()
            for func in sorted(dir(self))
            if func.startswith("run_")
        }

    async def _exec(self, fn):
        out = [None]
        await self.execute(lambda c: out.__setitem__(0, fn(c)))
        return out[0]


@pytest_asyncio.fixture(scope="session")
async def client():
    c = IntegrationClient()
    try:
        await c.connect()
    except OSError:
        pytest.skip("Unity server not available at localhost:8700")
    await c._exec(lambda c: c.add_abbreviated_classes(
        ["GameObject", "UniLiquidLinkIntegrationTest", "AssetDatabase"]))
    yield c
    await c.disconnect()
