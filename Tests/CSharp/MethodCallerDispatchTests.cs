using LLiquidLink;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using UniLiquidLink;
using UnityEngine;

[TestFixture]
public class MethodCallerDispatchTests
{
    RpcRegistry _rpc;
    MethodCaller _caller;

    [SetUp]
    public void SetUp()
    {
        var chain = new JsonSerializerChain();
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

    // ─── Register + Dispatch ─────────────────────────────────────────────────

    [Test]
    public void Dispatch_RegisteredFunc_ReturnsResult()
    {
        _rpc.Register("echo", (Func<int, int>)(x => x * 2));
        var result = (JsonElement)DispatchSync("echo", "21");
        Assert.AreEqual(42, result.GetInt32());
    }

    [Test]
    public void Dispatch_RegisteredStringFunc()
    {
        _rpc.Register("greet", (Func<string, string>)(name => "hello " + name));
        var result = (JsonElement)DispatchSync("greet", @"""world""");
        Assert.AreEqual("hello world", result.GetString());
    }

    [Test]
    public void Dispatch_Unknown_ThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() => _caller.Call("unknown", Array.Empty<JsonElement>()));
    }

    [Test]
    public void Dispatch_TooManyArgs_Throws()
    {
        _rpc.Register("noArgs", (Func<int>)(() => 42));
        var args = new JsonElement[] { JsonDocument.Parse("1").RootElement };
        Assert.Throws<ArgumentException>(() => _caller.Call("noArgs", args));
    }

    // ─── RegisterDirect + DispatchDirectWithObj ───────────────────────────────

    [Test]
    public void DispatchDirectWithObj_CompareTag_ReturnsTrue()
    {
        var go = new GameObject("__BusDirectTest");
        try
        {
            MethodInfo method = typeof(GameObject).GetMethod("CompareTag",
                new Type[] { typeof(string) });
            _rpc.RegisterDirect(
                "_compareTag",
                typeof(GameObject),
                "CompareTag",
                method.GetParameters(),
                args => ((GameObject)args[0]).CompareTag((string)args[1]),
                "UnityEngine.GameObject.CompareTag"
            );
            var restArgs = new JsonElement[] { JsonDocument.Parse(@"""Untagged""").RootElement };
            var result = _caller.CallDirectWithObj(go, "CompareTag", restArgs);
            Assert.AreEqual(true, result);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void DispatchDirectWithObj_UnknownMethod_ThrowsKeyNotFoundException()
    {
        var go = new GameObject("__BusDirectUnknown");
        try
        {
            Assert.Throws<KeyNotFoundException>(() =>
                _caller.CallDirectWithObj(go, "NoSuchDirectMethod", Array.Empty<JsonElement>()));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
