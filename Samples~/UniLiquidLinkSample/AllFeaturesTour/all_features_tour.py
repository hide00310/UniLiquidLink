"""
All Features Tour — a guided walkthrough of every LLiquidLink/UniLiquidLink feature.

Prerequisites:
  1. In Unity, run menu: UniLiquidLink/Samples/All Features Tour Server Start
  2. Run this script:
"""
import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Python~'))

from lliquidlink.client import Client, TcpJsonRpcTransport, RpcError
from lliquidlink.client.models import type_, enum


DEMO_OBJECT = "AllFeaturesTourObject"
DEMO_ASSET  = "Assets/AllFeaturesTourSample.mat"

def on_execute(client):
    # Register abbreviated class names so server-side methods can be called without
    # their full declaring-type prefix (e.g. client.Find(...) instead of
    # client.AllFeaturesTourServer.Find(...)).
    client.add_abbreviated_classes(["AllFeaturesTourServer", "GameObject", "AssetDatabase"])
    client.add_abbreviated_namespaces(["UnityEngine"])

    # ── Simple method calls ───────────────────────────────────────────────────
    print("=== Simple methods ===")
    print("SampleMethodInt(42):", client.SampleMethodInt(42))
    print("SampleMethodIntStr(42, 'hello'):", client.SampleMethodIntStr(42, "hello"))

    # ── GameObject.Find — direct call and class-chain call ────────────────────
    print("\n=== GameObject.Find ===")
    go = client.Find(DEMO_OBJECT)
    print("Find (abbreviated):", go)

    go2 = client.GameObject.Find(DEMO_OBJECT)
    print("Find (class chain):", go2)

    # ── Primitive values: int / str / bool ───────────────────────────────────
    print("\n=== Primitive round-trips ===")
    print("SamplePrimitiveObject(42):", client.SamplePrimitiveObject(42))
    print("SamplePrimitiveObject('hello'):", client.SamplePrimitiveObject("hello"))
    print("SamplePrimitiveObject(True):", client.SamplePrimitiveObject(True))

    # ── GameObject: single, array, list, dict ────────────────────────────────
    print("\n=== GameObject collections ===")
    print("SampleGameObject(go):", client.SampleGameObject(go))
    print("SampleGameObjectArray([go, go]):", client.SampleGameObjectArray([go, go]))
    print("SampleGameObjectList([go, go]):", client.SampleGameObjectList([go, go]))
    print("SampleGameObjectDict({'a':go,'b':go}):", client.SampleGameObjectDict({"a": go, "b": go}))

    # ── Vector3: single, array, list, dict ───────────────────────────────────
    print("\n=== Vector3 collections ===")
    v = {"x": 1, "y": 2, "z": 3}
    w = {"x": 4, "y": 5, "z": 6}
    print("SampleVector3:", client.SampleVector3(v))
    print("SampleVector3Array:", client.SampleVector3Array([v, w]))
    print("SampleVector3List:", client.SampleVector3List([v, w]))
    print("SampleVector3Dict:", client.SampleVector3Dict({"a": v, "b": w}))

    # ── Enum argument ─────────────────────────────────────────────────────────
    print("\n=== Enum argument ===")
    print("SampleEnum(enum('Beta')):", client.SampleEnum(enum("Beta")))

    # ── Property get ─────────────────────────────────────────────────────────
    print("\n=== Property get (chain resolution) ===")
    transform = go.transform()
    print("go.transform():", transform)
    print("go.transform.position():", go.transform.position())
    print("go.name():", go.name())
    print("go.activeSelf():", go.activeSelf())
    print("go.hideFlags():", go.hideFlags())

    # ── Property set (position), then restore ────────────────────────────────
    print("\n=== Property set (chain resolution) ===")
    go.transform.position = {"x": 1, "y": 2, "z": 3}
    print("After set, go.transform.position():", go.transform.position())
    go.transform.position = {"x": 0, "y": 0, "z": 0}
    print("Restored position:", go.transform.position())

    # ── Chained property access: transform.gameObject ────────────────────────
    print("\n=== Chained property ===")
    print("go.transform.gameObject():", go.transform.gameObject())

    # ── Direct instance method dispatch ──────────────────────────────────────
    print("\n=== Direct method dispatch (Rotate) ===")
    result = go.transform.Rotate(10, 20, 30)
    print("go.transform.Rotate(10, 20, 30):", result)

    # ── Load asset by type ────────────────────────────────────────────────────
    print("\n=== Asset loading with type_ ===")
    mat = client.LoadAssetAtPath(DEMO_ASSET, type_("Material"))
    print("LoadAssetAtPath:", mat)

    mat2 = client.AssetDatabase.LoadAssetAtPath(DEMO_ASSET, type_("Material"))
    print("AssetDatabase.LoadAssetAtPath:", mat2)

    # ── add_abbreviated_classes / add_abbreviated_namespaces ─────────────────
    # (already called at the top; the calls above show the effect)
    print("\n=== Abbreviated namespaces already active — type_('Material') worked above ===")

    # ── Nested static class via class-step chain ──────────────────────────────
    print("\n=== NestedMath.StaticAdd ===")
    print("NestedMath.StaticAdd(2, 3):", client.NestedMath.StaticAdd(2, 3))

    # ── Overload resolution ───────────────────────────────────────────────────
    print("\n=== Overload resolution ===")
    t = go.transform()
    print("SampleClass.ObjectOverload(go):", client.SampleClass.ObjectOverload(go))
    print("SampleClass.ObjectOverload(transform):", client.SampleClass.ObjectOverload(t))
    print("SampleClass.ObjectMethod(go):", client.SampleClass.ObjectMethod(go))

    # ── Error handling: calling a method that does not exist ──────────────────
    print("\n=== Error handling ===")
    try:
        client.NoSuchMethod999()
        print("ERROR: expected an exception but none was raised")
    except RpcError as e:
        print(f"RpcError caught (as expected): {e}")
    except Exception as e:
        print(f"Exception caught: {type(e).__name__}: {e}")

if __name__ == "__main__":
    client = Client(TcpJsonRpcTransport("localhost", 8700))
    client.on_execute += on_execute

    print("Connecting to Unity server...")
    client.mainloop()
    print("\nAll features tour complete.")
