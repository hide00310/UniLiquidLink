using LLiquidLink;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text.Json;
using UniLiquidLink;
using UnityEngine;

// Tests MethodCaller argument deserialization and result serialization,
// including the fallback JsonSerializerOptions path for Unity value types.
[TestFixture]
public class MethodCallerSerializationTests
{
    ObjectRegistry _registry;
    RpcRegistry _rpc;
    MethodCaller _caller;

    [SetUp]
    public void SetUp()
    {
        // JsonSerializerChain's main stage already disallows unmapped members, so Vector3 field
        // names cause a JsonException, triggering the fallback options for Unity serializable types.
        var chain = new JsonSerializerChain();
        var preJsonOptions = chain.Options[(int)JsonSerializerChain.Stage.Pre];
        var mainJsonOptions = chain.Options[(int)JsonSerializerChain.Stage.Main];
        var fallbackJsonOptions = chain.Options[(int)JsonSerializerChain.Stage.Fallback];

        _registry = new ObjectRegistry(() => new UniLiquidLink.Server.NullLogger());
        preJsonOptions.Converters.Add(new PreObjectConverter(_registry));
        mainJsonOptions.Converters.Add(new ObjectPrimitiveConverter());
        fallbackJsonOptions.Converters.Add(new JsonUtilityConverterFactory());
        _rpc = new RpcRegistry(() => new UniLiquidLink.Server.NullLogger());
        _caller = new MethodCaller(() => new UniLiquidLink.Server.NullLogger(), chain, _rpc.Searcher);
        _rpc.Register("JsonRpc_ResolveChain",
            (Func<RpcResolveChainParam, object>)_caller.JsonRpc_ResolveChain);
        _rpc.Register("JsonRpc_ResolveChainSet",
            (Func<RpcResolveChainSetParam, object>)_caller.JsonRpc_ResolveChainSet);
    }

    object DispatchSync(string method, params string[] jsonArgs)
    {
        var args = new JsonElement[jsonArgs.Length];
        for (int i = 0; i < jsonArgs.Length; i++)
        {
            args[i] = JsonDocument.Parse(jsonArgs[i]).RootElement;
        }

        return _caller.Call(method, args);
    }

    [Test]
    public void Dispatch_Int_DeserializesCorrectly()
    {
        _rpc.Register("id_int", (Func<int, int>)(x => x));
        var result = (JsonElement)DispatchSync("id_int", "7");
        Assert.AreEqual(7, result.GetInt32());
    }

    [Test]
    public void Dispatch_String_DeserializesCorrectly()
    {
        _rpc.Register("id_str", (Func<string, string>)(s => s));
        var result = (JsonElement)DispatchSync("id_str", @"""hello""");
        Assert.AreEqual("hello", result.GetString());
    }

    [Test]
    public void Dispatch_Vector3_FallbackDeserialize()
    {
        _rpc.Register("id_v3", (Func<Vector3, Vector3>)(v => v));
        var result = (JsonElement)DispatchSync("id_v3", @"{""x"":1.0,""y"":2.0,""z"":3.0}");
        Assert.AreEqual(1.0f, result.GetProperty("x").GetSingle(), 0.001f);
        Assert.AreEqual(2.0f, result.GetProperty("y").GetSingle(), 0.001f);
    }

    [Test]
    public void Dispatch_NullResult_ReturnsNull()
    {
        _rpc.Register("returns_null", (Func<object>)(() => null));
        var result = DispatchSync("returns_null");
        Assert.IsNull(result);
    }

    [Test]
    public void Dispatch_Vector3Array_FallbackDeserialize()
    {
        _rpc.Register("id_v3_array", (Func<Vector3[], Vector3[]>)(v => v));
        var result = (JsonElement)DispatchSync("id_v3_array", @"[{""x"":1.0,""y"":2.0,""z"":3.0},{""x"":4.0,""y"":5.0,""z"":6.0}]");
        Assert.AreEqual(JsonValueKind.Array, result.ValueKind);
        Assert.AreEqual(2, result.GetArrayLength());
        Assert.AreEqual(1.0f, result[0].GetProperty("x").GetSingle(), 0.001f);
        Assert.AreEqual(6.0f, result[1].GetProperty("z").GetSingle(), 0.001f);
    }

    [Test]
    public void Dispatch_Vector3List_FallbackDeserialize()
    {
        _rpc.Register("id_v3_list", (Func<List<Vector3>, List<Vector3>>)(v => v));
        var result = (JsonElement)DispatchSync("id_v3_list", @"[{""x"":1.0,""y"":2.0,""z"":3.0}]");
        Assert.AreEqual(JsonValueKind.Array, result.ValueKind);
        Assert.AreEqual(1, result.GetArrayLength());
        Assert.AreEqual(2.0f, result[0].GetProperty("y").GetSingle(), 0.001f);
    }

    [Test]
    public void Dispatch_Object_ResolvesFromRegistry()
    {
        var go = new GameObject("__DispatchObjRead");
        try
        {
            long id = _registry.RegisterObject(go);
            object captured = null;
            _rpc.Register("id_obj", (Func<object, int>)(o => { captured = o; return 0; }));
            string json = $@"{{""rpcType"":""{typeof(RpcInstanceObject).FullName}"",""instanceId"":{id},""orgType"":""UnityEngine.GameObject"",""name"":""__DispatchObjRead""}}";
            DispatchSync("id_obj", json);
            Assert.AreSame(go, captured);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void Dispatch_ObjectPrimitiveInt_FallsThroughToRawNumber()
    {
        object captured = null;
        _rpc.Register("id_obj_int", (Func<object, int>)(o => { captured = o; return 0; }));
        DispatchSync("id_obj_int", "42");
        Assert.AreEqual(42L, captured);
    }

    [Test]
    public void Dispatch_ObjectPrimitiveString_FallsThroughToRawString()
    {
        object captured = null;
        _rpc.Register("id_obj_str", (Func<object, int>)(o => { captured = o; return 0; }));
        DispatchSync("id_obj_str", @"""hello""");
        Assert.AreEqual("hello", captured);
    }

    [Test]
    public void Dispatch_ObjectPrimitiveResult_SerializesAsRawNumber()
    {
        _rpc.Register("id_obj_ret_int", (Func<object>)(() => 42));
        var result = (JsonElement)DispatchSync("id_obj_ret_int");
        Assert.AreEqual(JsonValueKind.Number, result.ValueKind);
        Assert.AreEqual(42, result.GetInt32());
    }

    [Test]
    public void Dispatch_ObjectPlainJsonObject_ThrowsJsonElementLeakException()
    {
        _rpc.Register("id_obj_plain", (Func<object, int>)(o => 0));
        Assert.Throws<JsonElementLeakException>(() => DispatchSync("id_obj_plain", @"{""foo"":1}"));
    }
}
