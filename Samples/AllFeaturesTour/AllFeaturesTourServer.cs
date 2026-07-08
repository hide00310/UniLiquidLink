using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UniLiquidLink.Samples
{
    public class AllFeaturesTourServer
    {
        const string DemoObjectName = "AllFeaturesTourObject";
        const string DemoAssetPath = "Assets/AllFeaturesTourSample.mat";

        internal const string PrefsKeyCommand = "UniLiquidLink.AllFeaturesTourServer.PythonServerStartCommand";

        static AllFeaturesTourServer _inner;
        readonly Server server;

        public static bool IsRunning
        {
            get { return _inner != null && _inner.server.IsRunning; }
        }

        public AllFeaturesTourServer(string pythonServerStartCommand)
        {
            SetupDemoScene();

            server = new Server(pythonServerStartCommand);
            // Register UnityEngine assemblies so type_("Material") etc. resolve correctly.
            server.RegisterCallerAssembly();

            // ── AddRpcMethod: static and instance methods ─────────────────────────
            //
            // Single int argument: client.SampleMethodInt(42)  →  returns 42
            server.Rpc.AddRpcMethod((Func<int, int>)SampleMethodInt);

            // Two arguments (int + string): client.SampleMethodIntStr(42, "hello")  →  returns 42
            server.Rpc.AddRpcMethod((Func<int, string, int>)SampleMethodIntStr);

            // Static method by class name:
            //   client.Find("AllFeaturesTourObject")           (with abbreviated class "GameObject")
            //   client.GameObject.Find("AllFeaturesTourObject") (explicit class-step chain)
            server.Rpc.AddRpcMethod((Func<string, GameObject>)GameObject.Find);

            // GameObject round-trip: client.SampleGameObject(go)  →  returns the same go
            server.Rpc.AddRpcMethod((Func<GameObject, GameObject>)SampleGameObject);

            // Primitive object (int / str / bool): client.SamplePrimitiveObject(42)
            server.Rpc.AddRpcMethod((Func<object, object>)SamplePrimitiveObject);

            // Vector3 round-trip: client.SampleVector3({"x":1,"y":2,"z":3})
            server.Rpc.AddRpcMethod((Func<Vector3, Vector3>)SampleVector3);

            // Array, List, and Dictionary of GameObjects
            server.Rpc.AddRpcMethod((Func<GameObject[], GameObject[]>)SampleGameObjectArray);
            server.Rpc.AddRpcMethod((Func<List<GameObject>, List<GameObject>>)SampleGameObjectList);
            server.Rpc.AddRpcMethod(
                (Func<Dictionary<string, GameObject>, Dictionary<string, GameObject>>)SampleGameObjectDict);

            // Array, List, and Dictionary of Vector3
            server.Rpc.AddRpcMethod((Func<Vector3[], Vector3[]>)SampleVector3Array);
            server.Rpc.AddRpcMethod((Func<List<Vector3>, List<Vector3>>)SampleVector3List);
            server.Rpc.AddRpcMethod(
                (Func<Dictionary<string, Vector3>, Dictionary<string, Vector3>>)SampleVector3Dict);

            // Enum argument: client.SampleEnum(enum("Beta"))  →  returns "Beta"
            server.Rpc.AddRpcMethod((Func<SampleTestEnum, SampleTestEnum>)SampleEnum);

            // Load an asset by path and type:
            //   client.LoadAssetAtPath("Assets/AllFeaturesTourSample.mat", type_("Material"))
            server.Rpc.AddRpcMethod((Func<string, Type, UnityEngine.Object>)AssetDatabase.LoadAssetAtPath);

            // ── AddRpcGetProperty / AddRpcSetProperty: chain resolution ───────────
            //
            // go.transform()  — returns the Transform proxy
            server.Rpc.AddRpcGetProperty((GameObject obj) => obj.transform);

            // go.name(), go.activeSelf(), go.hideFlags()
            server.Rpc.AddRpcGetProperty((GameObject obj) => obj.name);
            server.Rpc.AddRpcGetProperty((GameObject obj) => obj.activeSelf);
            server.Rpc.AddRpcGetProperty((GameObject obj) => obj.hideFlags);

            // go.transform.gameObject()  — round-trip back to the GameObject
            server.Rpc.AddRpcGetProperty((Transform obj) => obj.gameObject);

            // go.transform.position()  and  go.transform.position = {"x":1,"y":2,"z":3}
            server.Rpc.AddRpcGetProperty((Transform obj) => obj.position);
            server.Rpc.AddRpcSetProperty((Transform obj) => obj.position);

            // ── AddRpcDirectMethod: instance methods via chain ────────────────────
            //
            // go.transform.Rotate(10, 20, 30)
            // Space has a Unity default value and can be omitted by the caller.
            server.Rpc.AddRpcDirectMethod<Action<Transform, float, float, float, Space>>(
                (obj, x, y, z, s) => obj.Rotate(x, y, z, s)
            );

            // go.GetComponent(type_("Renderer"))
            server.Rpc.AddRpcDirectMethod<Func<GameObject, Type, Component>>(
                (obj, t) => obj.GetComponent(t)
            );

            // ── AddRpcAllMethod: register all public methods of a type ────────────
            //
            // client.NestedMath.StaticAdd(2, 3)  →  5
            server.Rpc.AddRpcAllMethod(typeof(NestedMath));

            // client.SampleClass.ObjectOverload(go)       — resolves to the GameObject overload
            // client.SampleClass.ObjectOverload(transform) — resolves to the Transform overload
            // client.SampleClass.ObjectMethod(go)          — succeeds; throws if not a GameObject
            server.Rpc.AddRpcAllMethod(typeof(SampleClass));
        }

        static void SetupDemoScene()
        {
            // Destroy any pre-existing demo object so every Start begins with a clean slate.
            var existing = GameObject.Find(DemoObjectName);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing);

            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = DemoObjectName;

            // Create the demo material asset the first time; reuse it on subsequent starts.
            string absPath = Path.Combine(Application.dataPath, "../" + DemoAssetPath);
            if (!File.Exists(absPath))
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.name = "AllFeaturesTourSample";
                AssetDatabase.CreateAsset(mat, DemoAssetPath);
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[AllFeaturesTourServer] Demo scene ready.");
        }

        public static void StartServer(string pythonServerStartCommand)
        {
            if (_inner != null)
                StopServer();

            _inner = new AllFeaturesTourServer(pythonServerStartCommand);
            _inner.server.Start();
            Debug.Log("[AllFeaturesTourServer] Started.");
        }

        public static void StopServer()
        {
            if (_inner == null)
                return;

            _inner.server.Stop();
            _inner = null;
            Debug.Log("[AllFeaturesTourServer] Stopped.");
        }

        // ── Helper methods registered as RPCs ─────────────────────────────────────

        public int SampleMethodInt(int x) { return x; }
        public int SampleMethodIntStr(int x, string s) { return x; }
        public GameObject SampleGameObject(GameObject x) { return x; }
        public object SamplePrimitiveObject(object x) { return x; }
        public Vector3 SampleVector3(Vector3 x) { return x; }
        public GameObject[] SampleGameObjectArray(GameObject[] x) { return x; }
        public List<GameObject> SampleGameObjectList(List<GameObject> x) { return x; }
        public Dictionary<string, GameObject> SampleGameObjectDict(
            Dictionary<string, GameObject> x) { return x; }
        public Vector3[] SampleVector3Array(Vector3[] x) { return x; }
        public List<Vector3> SampleVector3List(List<Vector3> x) { return x; }
        public Dictionary<string, Vector3> SampleVector3Dict(
            Dictionary<string, Vector3> x) { return x; }
        public SampleTestEnum SampleEnum(SampleTestEnum x) { return x; }

        public enum SampleTestEnum { Alpha, Beta, Gamma }

        // Nested static class — used with AddRpcAllMethod to demo static method registration.
        public static class NestedMath
        {
            public static int StaticAdd(int a, int b) { return a + b; }
        }

        // Class with overloads — used with AddRpcAllMethod to demo overload resolution.
        public class SampleClass
        {
            public static object ObjectOverload(GameObject obj) { return obj; }
            public static object ObjectOverload(Transform obj) { return obj; }

            // Throws if obj is not a GameObject, demonstrating server-side error propagation.
            public static object ObjectMethod(object obj)
            {
                if (obj.GetType() != typeof(GameObject))
                    throw new Exception(string.Format("{0} != GameObject", obj.GetType()));
                return obj;
            }
        }
    }
}
