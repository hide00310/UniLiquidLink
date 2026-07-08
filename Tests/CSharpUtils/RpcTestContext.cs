using LLiquidLink;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UniLiquidLink;
using UnityEditor;
using UnityEngine;

// Test harness that fully replicates Sample2's RPC setup for golden-file generation and regression tests.
public class RpcTestContext
{
    public List<string> Responses = new List<string>();

    readonly RpcRegistrar _rpc;
    readonly JsonRpcProtocol _protocol;
    readonly GoldenTestLogger _logger;

    public RpcTestContext()
    {
        _logger = new GoldenTestLogger();
        var jsonOptions = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var preJsonOptions = new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new ConverterOnlyResolver(),
        };
        var fallbackJsonOptions = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var chain = new JsonSerializerChain(preJsonOptions, jsonOptions, fallbackJsonOptions);
        var bus = new RpcBus(() => _logger, chain);

        var server = new UniLiquidLink.Server(string.Empty, new NullTransport(), new NullDispatcher());
        server._typeResolver.RegisterCallerAssembly();

        _rpc = new RpcRegistrar(bus, chain, () => _logger, typeResolver: server._typeResolver);
        // RPC dispatch errors are returned to the client as JSON-RPC error responses,
        // which tests inspect via Responses; do not rethrow them here.
        _protocol = new JsonRpcProtocol(
            bus,
            bytes => Responses.Add(ParseFrame(bytes)),
            () => _logger, jsonOptions, ex => { });

        _rpc.AddRpcConverter(new TypeConverter(server._typeResolver));
        _rpc.AddRpcConverter(new UnityObjectConverter(server._registry));
        _rpc.AddRpcConverter(new EnumConverter());
        _rpc.AddPreConverter(new PreObjectConverter(server._registry));
        _rpc.AddFallbackConverterFactory(new JsonUtilityConverterFactory());

        _rpc.AddRpcMethod((Func<int, int>)SampleMethodInt);
        _rpc.AddRpcMethod((Func<int, string, int>)SampleMethodIntStr);
        _rpc.AddRpcMethod((Func<string, GameObject>)GameObject.Find);
        _rpc.AddRpcMethod((Func<GameObject, GameObject>)SampleGameObject);
        _rpc.AddRpcMethod((Func<Vector3, Vector3>)SampleVector3);
        _rpc.AddRpcGetProperty((GameObject obj) => obj.transform);
        _rpc.AddRpcGetProperty((Transform obj) => obj.gameObject);
        _rpc.AddRpcGetProperty((Transform obj) => obj.position);
        _rpc.AddRpcSetProperty((Transform obj) => obj.position);
        _rpc.AddRpcDirectMethod<Action<Transform, float, float, float, Space>>(
            (obj, p1, p2, p3, p4) => obj.Rotate(p1, p2, p3, p4));
        _rpc.AddRpcMethod((Func<string, Type, UnityEngine.Object>)AssetDatabase.LoadAssetAtPath);
        _rpc.AddRpcDirectMethod<Func<GameObject, Type, Component>>((obj, p1) => obj.GetComponent(p1));
    }

    static int SampleMethodInt(int x) { return x; }
    static int SampleMethodIntStr(int x, string s) { return x; }
    static GameObject SampleGameObject(GameObject x) { return x; }
    static Vector3 SampleVector3(Vector3 x) { return x; }

    static byte[] BuildFrame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + body.Length];
        Array.Copy(body, 0, frame, 4, body.Length);
        return frame;
    }

    static string ParseFrame(byte[] frame)
    {
        return Encoding.UTF8.GetString(frame, 4, frame.Length - 4);
    }

    public string SendAndGetResponse(string json)
    {
        var frame = BuildFrame(json);
        _protocol.HandleMessage(frame);
        return Responses[^1];
    }

    public static string NormalizeInstanceIds(string json)
    {
        return Regex.Replace(json, @"""instanceId"":\s*-?\d+", @"""instanceId"":0");
    }
}
