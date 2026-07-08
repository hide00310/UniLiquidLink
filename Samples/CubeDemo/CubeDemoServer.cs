using System;
using UnityEngine;

namespace UniLiquidLink.Samples
{
    public class CubeDemoServer
    {
        internal const string PrefsKeyCommand = "UniLiquidLink.CubeDemoServer.PythonServerStartCommand";

        static CubeDemoServer _inner;
        readonly Server server;

        public static bool IsRunning
        {
            get { return _inner != null && _inner.server.IsRunning; }
        }

        public CubeDemoServer(string pythonServerStartCommand)
        {
            server = new Server(pythonServerStartCommand);
            // Register UnityEngine assemblies so type_("Renderer") resolves correctly.
            server.RegisterCallerAssembly();

            // Create a primitive: client.GameObject.CreatePrimitive(enum("Cube"))
            server.Rpc.AddRpcMethod((Func<PrimitiveType, GameObject>)GameObject.CreatePrimitive);

            // Property chain: cube.transform.Rotate(...) or cube.transform.position = ...
            server.Rpc.AddRpcGetProperty((GameObject obj) => obj.transform);

            // Direct instance dispatch: cube.GetComponent(type_("Renderer"))
            server.Rpc.AddRpcDirectMethod<Func<GameObject, Type, Component>>(
                (obj, t) => obj.GetComponent(t)
            );

            // Property chain: renderer.material.color = {...}
            server.Rpc.AddRpcGetProperty((Renderer r) => r.material);

            // Set color: renderer.material.color = {"r":1,"g":0,"b":0,"a":1}
            server.Rpc.AddRpcSetProperty((Material m) => m.color);

            // Rotate: cube.transform.Rotate(30, 45, 0)
            // Space parameter has a Unity default value and can be omitted by the caller.
            server.Rpc.AddRpcDirectMethod<Action<Transform, float, float, float, Space>>(
                (obj, x, y, z, s) => obj.Rotate(x, y, z, s)
            );
        }

        public static void StartServer(string pythonServerStartCommand)
        {
            if (_inner != null)
                StopServer();

            _inner = new CubeDemoServer(pythonServerStartCommand);
            _inner.server.Start();
            Debug.Log("[CubeDemoServer] Started.");
        }

        public static void StopServer()
        {
            if (_inner == null)
                return;

            _inner.server.Stop();
            _inner = null;
            Debug.Log("[CubeDemoServer] Stopped.");
        }
    }
}
