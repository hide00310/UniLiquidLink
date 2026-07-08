using LLiquidLink;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using UniLiquidLink;
using UnityEngine;

[TestFixture]
public class RpcBusDispatchTests
{
    RpcBus _bus;

    [SetUp]
    public void SetUp()
    {
        _bus = new RpcBus(
            () => new UniLiquidLink.Server.NullLogger(),
            new JsonSerializerChain(new JsonSerializerOptions(), new JsonSerializerOptions(), new JsonSerializerOptions()));
    }

    object DispatchSync(string method, params string[] jsonArgs)
    {
        var args = new JsonElement[jsonArgs.Length];
        for (int i = 0; i < jsonArgs.Length; i++)
        {
            args[i] = JsonDocument.Parse(jsonArgs[i]).RootElement;
        }

        return _bus.Dispatch(method, args);
    }

    // ─── Register + Dispatch ─────────────────────────────────────────────────

    [Test]
    public void Dispatch_RegisteredFunc_ReturnsResult()
    {
        _bus.Register("echo", (Func<int, int>)(x => x * 2));
        var result = (JsonElement)DispatchSync("echo", "21");
        Assert.AreEqual(42, result.GetInt32());
    }

    [Test]
    public void Dispatch_RegisteredStringFunc()
    {
        _bus.Register("greet", (Func<string, string>)(name => "hello " + name));
        var result = (JsonElement)DispatchSync("greet", @"""world""");
        Assert.AreEqual("hello world", result.GetString());
    }

    [Test]
    public void Dispatch_Unknown_ThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() => _bus.Dispatch("unknown", Array.Empty<JsonElement>()));
    }

    [Test]
    public void Dispatch_TooManyArgs_Throws()
    {
        _bus.Register("noArgs", (Func<int>)(() => 42));
        var args = new JsonElement[] { JsonDocument.Parse("1").RootElement };
        Assert.Throws<ArgumentException>(() => _bus.Dispatch("noArgs", args));
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
            _bus.RegisterDirect(
                "_compareTag",
                typeof(GameObject),
                "CompareTag",
                method.GetParameters(),
                args => ((GameObject)args[0]).CompareTag((string)args[1]),
                "UnityEngine.GameObject.CompareTag"
            );
            var restArgs = new JsonElement[] { JsonDocument.Parse(@"""Untagged""").RootElement };
            var result = _bus.DispatchDirectWithObj(go, "CompareTag", restArgs);
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
                _bus.DispatchDirectWithObj(go, "NoSuchDirectMethod", Array.Empty<JsonElement>()));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
