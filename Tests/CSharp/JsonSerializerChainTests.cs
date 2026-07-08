using LLiquidLink;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

// Tests the pre/main/fallback stage semantics of JsonSerializerChain in isolation,
// using scripted converters that record call order and simulate each stage's behavior.
[TestFixture]
public class JsonSerializerChainTests
{
    class Marker
    {
    }

    // Converter whose Read/Write behavior and call logging are injected per test.
    class ScriptedConverter : JsonConverter<Marker>
    {
        readonly string _stageName;
        readonly List<string> _log;
        readonly Func<Marker> _onRead;
        readonly Action _onWrite;

        public ScriptedConverter(string stageName, List<string> log, Func<Marker> onRead = null, Action onWrite = null)
        {
            _stageName = stageName;
            _log = log;
            _onRead = onRead ?? (() => null);
            _onWrite = onWrite ?? (() => { });
        }

        public override Marker Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            _log.Add(_stageName);
            reader.Skip();
            return _onRead();
        }

        public override void Write(Utf8JsonWriter writer, Marker value, JsonSerializerOptions options)
        {
            _log.Add(_stageName);
            _onWrite();
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }

    static JsonSerializerOptions BuildOptions(string stageName, List<string> log, Func<Marker> onRead = null, Action onWrite = null)
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new ScriptedConverter(stageName, log, onRead, onWrite));
        return opts;
    }

    // ─── Deserialize ───────────────────────────────────────────────────────

    [Test]
    public void Deserialize_PreSucceeds_ReturnsImmediatelyWithoutTryingOtherStages()
    {
        var log = new List<string>();
        var marker = new Marker();
        var pre = BuildOptions("pre", log, onRead: () => marker);
        var main = BuildOptions("main", log, onRead: () => throw new InvalidOperationException("main should not run"));
        var fallback = BuildOptions("fallback", log, onRead: () => throw new InvalidOperationException("fallback should not run"));
        var chain = new JsonSerializerChain(pre, main, fallback);

        object result = chain.Deserialize("{}", typeof(Marker));

        Assert.AreSame(marker, result);
        CollectionAssert.AreEqual(new[] { "pre" }, log);
    }

    [Test]
    public void Deserialize_PreReturnsNull_FallsThroughToMain()
    {
        var log = new List<string>();
        var marker = new Marker();
        var pre = BuildOptions("pre", log, onRead: () => null);
        var main = BuildOptions("main", log, onRead: () => marker);
        var fallback = BuildOptions("fallback", log, onRead: () => throw new InvalidOperationException("fallback should not run"));
        var chain = new JsonSerializerChain(pre, main, fallback);

        object result = chain.Deserialize("{}", typeof(Marker));

        Assert.AreSame(marker, result);
        CollectionAssert.AreEqual(new[] { "pre", "main" }, log);
    }

    [Test]
    public void Deserialize_MainReturnsNull_ReturnsNullWithoutTryingFallback()
    {
        var log = new List<string>();
        var pre = BuildOptions("pre", log, onRead: () => null);
        var main = BuildOptions("main", log, onRead: () => null);
        var fallback = BuildOptions("fallback", log, onRead: () => throw new InvalidOperationException("fallback should not run"));
        var chain = new JsonSerializerChain(pre, main, fallback);

        object result = chain.Deserialize("{}", typeof(Marker));

        Assert.IsNull(result);
        CollectionAssert.AreEqual(new[] { "pre", "main" }, log);
    }

    [Test]
    public void Deserialize_MainThrowsReadException_PropagatesWithoutTryingFallback()
    {
        var log = new List<string>();
        var pre = BuildOptions("pre", log, onRead: () => null);
        var main = BuildOptions("main", log, onRead: () => throw new RpcJsonConverterReadException("mismatch"));
        var fallback = BuildOptions("fallback", log, onRead: () => throw new InvalidOperationException("fallback should not run"));
        var chain = new JsonSerializerChain(pre, main, fallback);

        Assert.Throws<RpcJsonConverterReadException>(() => chain.Deserialize("{}", typeof(Marker)));
        CollectionAssert.AreEqual(new[] { "pre", "main" }, log);
    }

    [Test]
    public void Deserialize_MainThrowsOtherException_FallsThroughToFallback()
    {
        var log = new List<string>();
        var marker = new Marker();
        var pre = BuildOptions("pre", log, onRead: () => null);
        var main = BuildOptions("main", log, onRead: () => throw new InvalidOperationException("boom"));
        var fallback = BuildOptions("fallback", log, onRead: () => marker);
        var chain = new JsonSerializerChain(pre, main, fallback);

        object result = chain.Deserialize("{}", typeof(Marker));

        Assert.AreSame(marker, result);
        CollectionAssert.AreEqual(new[] { "pre", "main", "fallback" }, log);
    }

    [Test]
    public void Deserialize_PreThrows_ExceptionSwallowedAndFallsThroughToMain()
    {
        var log = new List<string>();
        var marker = new Marker();
        var pre = BuildOptions("pre", log, onRead: () => throw new InvalidOperationException("pre boom"));
        var main = BuildOptions("main", log, onRead: () => marker);
        var fallback = BuildOptions("fallback", log, onRead: () => throw new InvalidOperationException("fallback should not run"));
        var chain = new JsonSerializerChain(pre, main, fallback);

        object result = chain.Deserialize("{}", typeof(Marker));

        Assert.AreSame(marker, result);
        CollectionAssert.AreEqual(new[] { "pre", "main" }, log);
    }

    [Test]
    public void Deserialize_ObjectTypeWithNoConverter_ThrowsJsonElementLeakException()
    {
        var pre = new JsonSerializerOptions();
        var main = new JsonSerializerOptions();
        var fallback = new JsonSerializerOptions();
        var chain = new JsonSerializerChain(pre, main, fallback);

        Assert.Throws<JsonElementLeakException>(() => chain.Deserialize("{}", typeof(object)));
    }

    [Test]
    public void Deserialize_JsonElementTargetType_DoesNotThrow()
    {
        var pre = new JsonSerializerOptions();
        var main = new JsonSerializerOptions();
        var fallback = new JsonSerializerOptions();
        var chain = new JsonSerializerChain(pre, main, fallback);

        object result = chain.Deserialize(@"{""a"":1}", typeof(JsonElement));

        Assert.IsInstanceOf<JsonElement>(result);
        Assert.AreEqual(1, ((JsonElement)result).GetProperty("a").GetInt32());
    }

    // ─── SerializeToElement ────────────────────────────────────────────────

    [Test]
    public void Serialize_PreSucceeds_ReturnsImmediatelyWithoutTryingOtherStages()
    {
        var log = new List<string>();
        var pre = BuildOptions("pre", log);
        var main = BuildOptions("main", log, onWrite: () => throw new InvalidOperationException("main should not run"));
        var fallback = BuildOptions("fallback", log, onWrite: () => throw new InvalidOperationException("fallback should not run"));
        var chain = new JsonSerializerChain(pre, main, fallback);

        chain.SerializeToElement(new Marker(), typeof(Marker));

        CollectionAssert.AreEqual(new[] { "pre" }, log);
    }

    [Test]
    public void Serialize_PreThrows_FallsThroughToMain()
    {
        var log = new List<string>();
        var pre = BuildOptions("pre", log, onWrite: () => throw new InvalidOperationException("pre boom"));
        var main = BuildOptions("main", log);
        var fallback = BuildOptions("fallback", log, onWrite: () => throw new InvalidOperationException("fallback should not run"));
        var chain = new JsonSerializerChain(pre, main, fallback);

        chain.SerializeToElement(new Marker(), typeof(Marker));

        CollectionAssert.AreEqual(new[] { "pre", "main" }, log);
    }

    [Test]
    public void Serialize_MainThrows_FallsThroughToFallback()
    {
        var log = new List<string>();
        var pre = BuildOptions("pre", log, onWrite: () => throw new InvalidOperationException("pre boom"));
        var main = BuildOptions("main", log, onWrite: () => throw new InvalidOperationException("main boom"));
        var fallback = BuildOptions("fallback", log);
        var chain = new JsonSerializerChain(pre, main, fallback);

        chain.SerializeToElement(new Marker(), typeof(Marker));

        CollectionAssert.AreEqual(new[] { "pre", "main", "fallback" }, log);
    }
}
