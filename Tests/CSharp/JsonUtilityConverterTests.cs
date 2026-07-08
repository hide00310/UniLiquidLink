using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text.Json;
using UniLiquidLink;
using UnityEngine;

[TestFixture]
public class JsonUtilityConverterTests
{
    JsonSerializerOptions _opts;

    [SetUp]
    public void SetUp()
    {
        _opts = new JsonSerializerOptions();
        _opts.Converters.Add(new JsonUtilityConverterFactory());
    }

    // ─── CanConvert boundary ───────────────────────────────────────────────────

    [Test]
    public void CanConvert_ValueStruct_ReturnsTrue()
    {
        Assert.IsTrue(new JsonUtilityConverterFactory().CanConvert(typeof(Vector3)));
    }

    [Test]
    public void CanConvert_PrimitiveOrEnum_ReturnsFalse()
    {
        var factory = new JsonUtilityConverterFactory();
        Assert.IsFalse(factory.CanConvert(typeof(int)));
        Assert.IsFalse(factory.CanConvert(typeof(DayOfWeek)));
    }

    [Test]
    public void CanConvert_ReferenceType_ReturnsFalse()
    {
        var factory = new JsonUtilityConverterFactory();
        Assert.IsFalse(factory.CanConvert(typeof(string)));
        Assert.IsFalse(factory.CanConvert(typeof(List<Vector3>)));
        Assert.IsFalse(factory.CanConvert(typeof(Vector3[])));
    }

    [Test]
    public void CanConvert_NullableValueStruct_ReturnsFalse()
    {
        // Excluded so STJ unwraps Nullable<T> down to T, which the factory then matches.
        Assert.IsFalse(new JsonUtilityConverterFactory().CanConvert(typeof(Vector3?)));
    }

    [Test]
    public void CanConvert_SystemValueStruct_ReturnsTrue()
    {
        // Documents the accepted risk: every non-primitive value struct is routed to JsonUtility
        // on the fallback options, including BCL types like DateTime, not just UnityEngine ones.
        Assert.IsTrue(new JsonUtilityConverterFactory().CanConvert(typeof(DateTime)));
    }

    // ─── Single value round-trip ────────────────────────────────────────────────

    [Test]
    public void Vector3_Write_ProducesFieldJson()
    {
        string json = JsonSerializer.Serialize(new Vector3(1, 2, 3), _opts);
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.AreEqual(1.0f, doc.GetProperty("x").GetSingle(), 0.001f);
        Assert.AreEqual(3.0f, doc.GetProperty("z").GetSingle(), 0.001f);
    }

    [Test]
    public void Vector3_RoundTrip()
    {
        var original = new Vector3(1.5f, -2.5f, 3.25f);
        string json = JsonSerializer.Serialize(original, _opts);
        var result = JsonSerializer.Deserialize<Vector3>(json, _opts);
        Assert.AreEqual(original, result);
    }

    // ─── Collections: the core of this change — STJ discovers these natively,  ──
    // ─── recursing into JsonUtilityConverter<Vector3> element by element.       ──

    [Test]
    public void Vector3Array_RoundTrip()
    {
        var original = new[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6) };
        string json = JsonSerializer.Serialize(original, _opts);
        var result = JsonSerializer.Deserialize<Vector3[]>(json, _opts);
        CollectionAssert.AreEqual(original, result);
    }

    [Test]
    public void Vector3List_RoundTrip()
    {
        var original = new List<Vector3> { new Vector3(1, 2, 3), new Vector3(4, 5, 6) };
        string json = JsonSerializer.Serialize(original, _opts);
        var result = JsonSerializer.Deserialize<List<Vector3>>(json, _opts);
        CollectionAssert.AreEqual(original, result);
    }

    [Test]
    public void Vector3Dictionary_RoundTrip()
    {
        var original = new Dictionary<string, Vector3>
        {
            ["a"] = new Vector3(1, 2, 3),
            ["b"] = new Vector3(4, 5, 6),
        };
        string json = JsonSerializer.Serialize(original, _opts);
        var result = JsonSerializer.Deserialize<Dictionary<string, Vector3>>(json, _opts);
        Assert.AreEqual(original["a"], result["a"]);
        Assert.AreEqual(original["b"], result["b"]);
    }

    [Test]
    public void NullableVector3_WithValue_RoundTrip()
    {
        Vector3? original = new Vector3(1, 2, 3);
        string json = JsonSerializer.Serialize(original, _opts);
        var result = JsonSerializer.Deserialize<Vector3?>(json, _opts);
        Assert.AreEqual(original, result);
    }

    [Test]
    public void NullableVector3_Null_RoundTrip()
    {
        Vector3? original = null;
        string json = JsonSerializer.Serialize(original, _opts);
        var result = JsonSerializer.Deserialize<Vector3?>(json, _opts);
        Assert.IsNull(result);
    }
}
