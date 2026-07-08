using LLiquidLink;
using NUnit.Framework;
using System;
using System.Text.Json;
using UniLiquidLink;
using UnityEngine;

[TestFixture]
public class ConverterTests
{
    TypeResolver _resolver;
    ObjectRegistry _reg;
    JsonSerializerOptions _opts;
    JsonSerializerOptions _primitiveOpts;

    [SetUp]
    public void SetUp()
    {
        _resolver = new TypeResolver(() => new UniLiquidLink.Server.NullLogger());
        _resolver.RegisterAssembly(typeof(string).Assembly);
        _resolver.RegisterAssembly(typeof(UnityEngine.GameObject).Assembly);

        _reg = new ObjectRegistry(() => new UniLiquidLink.Server.NullLogger());

        _opts = new JsonSerializerOptions();
        _opts.Converters.Add(new TypeConverter(_resolver));
        _opts.Converters.Add(new EnumConverter());
        _opts.Converters.Add(new PreObjectConverter(_reg));

        _primitiveOpts = new JsonSerializerOptions();
        _primitiveOpts.Converters.Add(new ObjectPrimitiveConverter());
    }

    // Enum type used only by EnumConverter tests below.
    enum SampleEnum { Alpha, Beta, Gamma }

    // ─── TypeConverter ───────────────────────────────────────────────────────

    [Test]
    public void TypeConverter_Write_IncludesFullName()
    {
        string json = JsonSerializer.Serialize(typeof(int), _opts);
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.AreEqual("System.Int32", doc.GetProperty("value").GetString());
    }

    [Test]
    public void TypeConverter_Read_ResolvesType()
    {
        var type = JsonSerializer.Deserialize<Type>(@"{""type"":""type"",""value"":""System.Int32""}", _opts);
        Assert.AreEqual(typeof(int), type);
    }

    [Test]
    public void TypeConverter_Write_NullType_ProducesJsonNull()
    {
        string json = JsonSerializer.Serialize<Type>(null, _opts);
        Assert.AreEqual("null", json);
    }

    // ─── UnityObjectConverter ────────────────────────────────────────────────

    [Test]
    public void UnityObjectConverter_Write_ProducesRpcUnityObjectFields()
    {
        var go = new GameObject("__ConvWrite");
        try
        {
            string json = JsonSerializer.Serialize<UnityEngine.Object>(go, _opts);
            var doc = JsonDocument.Parse(json).RootElement;
            Assert.AreEqual("UnityEngine.GameObject", doc.GetProperty("orgType").GetString());
            Assert.AreEqual("__ConvWrite", doc.GetProperty("name").GetString());
            Assert.IsTrue(doc.TryGetProperty("instanceId", out _));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void UnityObjectConverter_Read_ResolvesObjectFromRegistry()
    {
        var go = new GameObject("__ConvRead");
        try
        {
            long id = _reg.RegisterObject(go);
            string json = $@"{{""rpcType"":""{typeof(RpcUnityObject).FullName}"",""instanceId"":{id},""orgType"":""UnityEngine.GameObject"",""name"":""__ConvRead"",""attributes"":[""InstanceObject""]}}";
            var result = JsonSerializer.Deserialize<UnityEngine.Object>(json, _opts);
            Assert.AreEqual(go, result);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void UnityObjectConverter_Read_UnknownId_ThrowsArgumentException()
    {
        string json = $@"{{""rpcType"":""{typeof(RpcUnityObject).FullName}"",""instanceId"":99999999,""orgType"":""UnityEngine.GameObject"",""name"":""missing"",""attributes"":[""InstanceObject""]}}";
        Assert.Throws<ArgumentException>(() =>
            JsonSerializer.Deserialize<UnityEngine.Object>(json, _opts));
    }

    // ─── PreObjectConverter ──────────────────────────────────────────────────

    [Test]
    public void PreObjectConverter_Write_ProducesRpcUnityObjectFields()
    {
        var go = new GameObject("__PreConvWrite");
        try
        {
            string json = JsonSerializer.Serialize<object>(go, _opts);
            var doc = JsonDocument.Parse(json).RootElement;
            Assert.AreEqual("UnityEngine.GameObject", doc.GetProperty("orgType").GetString());
            Assert.AreEqual(go.ToString(), doc.GetProperty("name").GetString());
            Assert.IsTrue(doc.TryGetProperty("instanceId", out _));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void PreObjectConverter_Read_ResolvesObjectFromRegistry()
    {
        var go = new GameObject("__PreConvRead");
        try
        {
            long id = _reg.RegisterObject(go);
            string json = $@"{{""rpcType"":""{typeof(RpcUnityObject).FullName}"",""instanceId"":{id},""orgType"":""UnityEngine.GameObject"",""name"":""__PreConvRead""}}";
            var result = JsonSerializer.Deserialize<object>(json, _opts);
            Assert.AreSame(go, result);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void PreObjectConverter_Read_UnknownId_ThrowsArgumentException()
    {
        string json = $@"{{""rpcType"":""{typeof(RpcUnityObject).FullName}"",""instanceId"":99999999,""orgType"":""UnityEngine.GameObject"",""name"":""missing""}}";
        Assert.Throws<ArgumentException>(() =>
            JsonSerializer.Deserialize<object>(json, _opts));
    }

    [Test]
    public void PreObjectConverter_Read_OrgTypeMismatch_ThrowsRpcJsonConverterReadException()
    {
        var go = new GameObject("__PreConvMismatch");
        try
        {
            long id = _reg.RegisterObject(go);
            string json = $@"{{""rpcType"":""{typeof(RpcUnityObject).FullName}"",""instanceId"":{id},""orgType"":""UnityEngine.Transform"",""name"":""__PreConvMismatch""}}";
            Assert.Throws<RpcJsonConverterReadException>(() =>
                JsonSerializer.Deserialize<object>(json, _opts));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void PreObjectConverter_Write_PrimitiveValue_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Serialize<object>(42, _opts));
    }

    // ─── ObjectPrimitiveConverter ────────────────────────────────────────────

    [Test]
    public void ObjectPrimitiveConverter_WriteRead_Int_RoundTripsAsRawNumber()
    {
        string json = JsonSerializer.Serialize<object>(42, _primitiveOpts);
        Assert.AreEqual("42", json);
        object result = JsonSerializer.Deserialize<object>(json, _primitiveOpts);
        Assert.AreEqual(42L, result);
    }

    [Test]
    public void ObjectPrimitiveConverter_WriteRead_String_RoundTripsAsRawString()
    {
        string json = JsonSerializer.Serialize<object>("hello", _primitiveOpts);
        Assert.AreEqual("\"hello\"", json);
        object result = JsonSerializer.Deserialize<object>(json, _primitiveOpts);
        Assert.AreEqual("hello", result);
    }

    [Test]
    public void ObjectPrimitiveConverter_WriteRead_Bool_RoundTripsAsRawBool()
    {
        string json = JsonSerializer.Serialize<object>(true, _primitiveOpts);
        Assert.AreEqual("true", json);
        object result = JsonSerializer.Deserialize<object>(json, _primitiveOpts);
        Assert.AreEqual(true, result);
    }

    [Test]
    public void ObjectPrimitiveConverter_Read_Null_ReturnsNull()
    {
        object result = JsonSerializer.Deserialize<object>("null", _primitiveOpts);
        Assert.IsNull(result);
    }

    // ─── EnumConverter ───────────────────────────────────────────────────────

    [Test]
    public void EnumConverter_Write_ProducesRpcEnumValue()
    {
        string json = JsonSerializer.Serialize(SampleEnum.Beta, _opts);
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.AreEqual("Beta", doc.GetProperty("value").GetString());
    }

    [Test]
    public void EnumConverter_Read_ParsesEnumValue()
    {
        var result = JsonSerializer.Deserialize<SampleEnum>(@"{""value"":""Beta"",""rpcEnum"":1}", _opts);
        Assert.AreEqual(SampleEnum.Beta, result);
    }

    [Test]
    public void EnumConverter_Read_UnknownValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            JsonSerializer.Deserialize<SampleEnum>(@"{""value"":""NoSuchValue"",""rpcEnum"":1}", _opts));
    }
}
