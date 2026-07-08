using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UniLiquidLink
{
    public class UniLiquidLinkIntegrationTest
    {
        internal const string PrefsKeyCommand = "UniLiquidLink.IntegrationTest.PythonServerStartCommand";

        // Start is called before the first frame update
        static UniLiquidLinkIntegrationTest _inner;
        readonly Server server;

        public Transform Obj;
        public int DebugProp = 456;
        internal ListLogger listLogger;

        public static bool IsRunning
        {
            get { return _inner != null && _inner.server.IsRunning; }
        }

        public UniLiquidLinkIntegrationTest(string pythonServerStartCommand)
        {
            server = new Server(pythonServerStartCommand);
            listLogger = new ListLogger();
            server.Logger = listLogger;
            server.OnError += (ex) => ExceptionDispatchInfo.Capture(ex).Throw();
            server.OnDisconnect += (id) => DebugStop();

            server.RegisterCallerAssembly();

            server.Rpc.AddRpcMethod((Func<int, int>)SampleMethodInt);
            server.Rpc.AddRpcMethod((Func<int, string, int>)SampleMethodIntStr);
            server.Rpc.AddRpcMethod((Func<string, GameObject>)GameObject.Find);
            server.Rpc.AddRpcMethod((Func<GameObject, GameObject>)SampleGameObject);
            server.Rpc.AddRpcMethod((Func<object, object>)SamplePrimitiveObject);
            server.Rpc.AddRpcMethod((Func<Vector3, Vector3>)SampleVector3);
            server.Rpc.AddRpcMethod((Func<GameObject[], GameObject[]>)SampleGameObjectArray);
            server.Rpc.AddRpcMethod((Func<List<GameObject>, List<GameObject>>)SampleGameObjectList);
            server.Rpc.AddRpcMethod((Func<Dictionary<string, GameObject>, Dictionary<string, GameObject>>)SampleGameObjectDict);
            server.Rpc.AddRpcMethod((Func<Vector3[], Vector3[]>)SampleVector3Array);
            server.Rpc.AddRpcMethod((Func<List<Vector3>, List<Vector3>>)SampleVector3List);
            server.Rpc.AddRpcMethod((Func<Dictionary<string, Vector3>, Dictionary<string, Vector3>>)SampleVector3Dict);
            server.Rpc.AddRpcMethod((Func<SampleTestEnum, SampleTestEnum>)SampleEnum);

            server.Rpc.AddRpcGetProperty((GameObject obj) => obj.transform);

            server.Rpc.AddRpcGetProperty((GameObject obj) => obj.name);
            server.Rpc.AddRpcGetProperty((GameObject obj) => obj.activeSelf);
            server.Rpc.AddRpcGetProperty((GameObject obj) => obj.hideFlags);


            server.Rpc.AddRpcGetProperty((Transform obj) => obj.gameObject);
            server.Rpc.AddRpcGetProperty((Transform obj) => obj.position);
            server.Rpc.AddRpcSetProperty((Transform obj) => obj.position);
            server.Rpc.AddRpcDirectMethod<Action<Transform, float, float, float, Space>>(
                (obj, p1, p2, p3, p4) => obj.Rotate(p1, p2, p3, p4)
            );
            server.Rpc.AddRpcMethod((Func<string, Type, UnityEngine.Object>)AssetDatabase.LoadAssetAtPath);
            server.Rpc.AddRpcDirectMethod<Func<GameObject, Type, Component>>((obj, p1) => obj.GetComponent(p1));
            server.Rpc.AddRpcAllMethod(typeof(NestedMath));
            server.Rpc.AddRpcAllMethod(typeof(SampleClass));
        }

        // nested static class used by the class-step chain integration scenario
        public static class NestedMath
        {
            public static int StaticAdd(int a, int b) { return a + b; }
        }

        // enum used by the SampleEnum RPC method to test enum-typed arguments
        public enum SampleTestEnum { Alpha, Beta, Gamma }

        public int SampleMethodInt(int x)
        {
            Debug.LogError($"Call SampleMethodInt {x}");
            return x;
        }

        public int SampleMethodIntStr(int x, string s)
        {
            Debug.LogError($"Call SampleMethodIntStr {x}, {s}");
            return x;
        }

        public GameObject SampleGameObject(GameObject x)
        {
            Debug.LogError($"Call SampleGameObject {x}");
            return x;
        }

        public object SamplePrimitiveObject(object x)
        {
            Debug.LogError($"Call SamplePrimitiveObject {x}");
            return x;
        }

        public Vector3 SampleVector3(Vector3 x)
        {
            Debug.LogError($"Call SampleVector3 {x}");
            return x;
        }

        public GameObject[] SampleGameObjectArray(GameObject[] x)
        {
            Debug.LogError($"Call SampleGameObject {x}");
            return x;
        }
        public List<GameObject> SampleGameObjectList(List<GameObject> x)
        {
            Debug.LogError($"Call SampleGameObject {x}");
            return x;
        }
        public Dictionary<string, GameObject> SampleGameObjectDict(Dictionary<string, GameObject> x)
        {
            Debug.LogError($"Call SampleGameObject {x}");
            return x;
        }

        public Vector3[] SampleVector3Array(Vector3[] x)
        {
            Debug.LogError($"Call SampleVector3Array {x}");
            return x;
        }
        public List<Vector3> SampleVector3List(List<Vector3> x)
        {
            Debug.LogError($"Call SampleVector3List {x}");
            return x;
        }
        public Dictionary<string, Vector3> SampleVector3Dict(Dictionary<string, Vector3> x)
        {
            Debug.LogError($"Call SampleVector3Dict {x}");
            return x;
        }

        public SampleTestEnum SampleEnum(SampleTestEnum x)
        {
            Debug.LogError($"Call SampleEnum {x}");
            return x;
        }

        public class SampleClass
        {
            public static object ObjectOverload(GameObject obj)
            {
                Debug.LogError($"ObjectOverload {obj}");
                return obj;
            }
            public static object ObjectOverload(Transform obj)
            {
                Debug.LogError($"ObjectOverload {obj}");
                return obj;
            }
            public static object ObjectMethod(object obj)
            {
                Debug.LogError($"ObjectMethod {obj}");
                return obj.GetType() != typeof(GameObject) ? throw new Exception($"{obj.GetType()} != GameObject") : obj;
            }
        }

        [MenuItem("UniLiquidLink/Tests/Integration Test Server Start")]
        public static void DebugStart()
        {
            if (_inner != null)
            {
                DebugStop();
            }

            string pythonServerStartCommand = EditorPrefs.GetString(PrefsKeyCommand, "");
            if (string.IsNullOrEmpty(pythonServerStartCommand))
            {
                throw new InvalidOperationException(
                    "pythonServerStartCommand is not configured. Open " +
                    "UniLiquidLink/Integration Test Server Window and set Python Server Start Command, " +
                    "or set EditorPrefs key \"" + PrefsKeyCommand + "\" directly.");
            }

            SetupIntegrationTest.Setup();
            _inner = new UniLiquidLinkIntegrationTest(pythonServerStartCommand);
            _inner.server.Start();
        }

        [MenuItem("UniLiquidLink/Tests/Integration Test Server Stop")]
        public static void DebugStop()
        {
            Debug.LogError("DebugStop");
            if (_inner == null)
            {
                return;
            }

            _inner.server.Stop();
            string logPath = UniLiquidLinkTestHelper.GetSourceDir() + "/Sample2.txt";
            string outTxt = string.Join("\n", _inner.listLogger.Entries);
            outTxt = Regex.Replace(outTxt, @"""instanceId"":\s*-?\d+", @"""instanceId"":0");
            System.IO.File.WriteAllText(logPath, outTxt);
            _inner = null;
        }

    }
}
