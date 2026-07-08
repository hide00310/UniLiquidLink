using LLiquidLink;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UniLiquidLink;

[TestFixture]
public class RpcRegistrarAllTests
{
    RpcBus _bus;
    RpcRegistrar _rpc;
    JsonSerializerOptions _options;
    JsonSerializerOptions _fallback;
    JsonSerializerOptions _pre;
    JsonSerializerChain _chain;

    [SetUp]
    public void SetUp()
    {
        _options = new JsonSerializerOptions();
        _fallback = new JsonSerializerOptions();
        _pre = new JsonSerializerOptions();
        _chain = new JsonSerializerChain(_pre, _options, _fallback);
        _bus = new RpcBus(() => new UniLiquidLink.Server.NullLogger(), _chain);
        _rpc = new RpcRegistrar(_bus, _chain, () => new UniLiquidLink.Server.NullLogger());
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

    // RPC name for FullName mode; AddRpcAll* normalizes nested-type '+' separators to '.'.
    static string Rpc(Type type, string method)
    {
        return type.FullName.Replace('+', '.') + "." + method;
    }

    // ─── Sample types ────────────────────────────────────────────────────────

    public class Calc
    {
        public int Add(int a) { return a; }
        public int Add(int a, int b) { return a + b; }
        public string Echo(string s) { return s; }
        public int Echo(int n) { return n; }
        public static int Square(int x) { return x * x; }
        public T Generic<T>(T v) { return v; }
        public int Prop { get; set; }
    }

    public class Base
    {
        public int BaseMethod() { return 1; }
    }

    public class Derived : Base
    {
        public int DerivedMethod() { return 2; }
    }

    public class Foo
    {
        public int Value;
        public int Doubled => Value * 2;
        public static int Counter;
    }

    // Type with public nested types for nested-registration tests.
    public class Outer
    {
        public int OuterMethod() { return 10; }
        public class Inner
        {
            public static int InnerStatic(int x) { return x + 1; }
            public class Deep
            {
                public static int DeepStatic() { return 99; }
            }
        }
    }

    // Outer type with a nested type carrying readable members for chain resolution.
    public class Container
    {
        public class Item
        {
            public int Value;
            public int Tripled => Value * 3;
        }
    }

    // Marker wire type and converter for Container.Item chain resolution.
    public class ItemRpc { }

    public class ItemConverter : RpcJsonConverter<Container.Item, ItemRpc>
    {
        public override Container.Item Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var item = new Container.Item();
            if (doc.RootElement.TryGetProperty("Value", out var v))
            {
                item.Value = v.GetInt32();
            }

            return item;
        }

        public override void Write(Utf8JsonWriter writer, Container.Item value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("rpcType", typeof(ItemRpc).FullName);
            writer.WriteNumber("Value", value.Value);
            writer.WriteEndObject();
        }
    }

    // Marker wire type; its FullName is used as the rpcType discriminator.
    public class FooRpc { }

    // Minimal converter so chain resolution can deserialize a Foo root object.
    public class FooConverter : RpcJsonConverter<Foo, FooRpc>
    {
        public override Foo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var foo = new Foo();
            if (doc.RootElement.TryGetProperty("Value", out var v))
            {
                foo.Value = v.GetInt32();
            }

            return foo;
        }

        public override void Write(Utf8JsonWriter writer, Foo value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("rpcType", typeof(FooRpc).FullName);
            writer.WriteNumber("Value", value.Value);
            writer.WriteEndObject();
        }
    }

    // Marker wire type registered against the coarse Base type, mirroring how UnityObjectConverter
    // is always registered against UnityEngine.Object regardless of the concrete instance.
    public class BaseRpc { }

    // Converter whose registered org type (Base) is deliberately coarser than the concrete instances
    // it may receive. Read materializes whatever typeToConvert it is given, so tests can prove
    // DeserializeRoot resolved the concrete orgType instead of trusting the registered Base type.
    public class BaseConverter : RpcJsonConverter<Base, BaseRpc>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeof(Base).IsAssignableFrom(typeToConvert);
        }

        public override Base Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using (JsonDocument.ParseValue(ref reader)) { }
            return (Base)Activator.CreateInstance(typeToConvert);
        }

        public override void Write(Utf8JsonWriter writer, Base value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("rpcType", typeof(BaseRpc).FullName);
            writer.WriteEndObject();
        }
    }

    // ─── AddRpcAllMethod: static / instance ──────────────────────────────────

    [Test]
    public void AddRpcAllMethod_StaticMethod_Dispatches()
    {
        _rpc.AddRpcAllMethod(typeof(Calc));
        var result = (JsonElement)DispatchSync(Rpc(typeof(Calc), "Square"), "5");
        Assert.AreEqual(25, result.GetInt32());
    }

    [Test]
    public void AddRpcAllMethod_InstanceMethod_FirstArgIsInstance()
    {
        _rpc.AddRpcAllMethod(typeof(Calc));
        // {} deserializes to a fresh Calc instance; remaining args are the parameters.
        var result = (JsonElement)DispatchSync(Rpc(typeof(Calc), "Add"), "{}", "2", "3");
        Assert.AreEqual(5, result.GetInt32());
    }

    // ─── Overload resolution ─────────────────────────────────────────────────

    [Test]
    public void AddRpcAllMethod_OverloadByArity_PicksMatching()
    {
        _rpc.AddRpcAllMethod(typeof(Calc));
        var one = (JsonElement)DispatchSync(Rpc(typeof(Calc), "Add"), "{}", "7");
        Assert.AreEqual(7, one.GetInt32());
        var two = (JsonElement)DispatchSync(Rpc(typeof(Calc), "Add"), "{}", "4", "6");
        Assert.AreEqual(10, two.GetInt32());
    }

    [Test]
    public void AddRpcAllMethod_OverloadByType_PicksMatching()
    {
        _rpc.AddRpcAllMethod(typeof(Calc));
        var asInt = (JsonElement)DispatchSync(Rpc(typeof(Calc), "Echo"), "{}", "42");
        Assert.AreEqual(JsonValueKind.Number, asInt.ValueKind);
        Assert.AreEqual(42, asInt.GetInt32());
        var asStr = (JsonElement)DispatchSync(Rpc(typeof(Calc), "Echo"), "{}", @"""hi""");
        Assert.AreEqual(JsonValueKind.String, asStr.ValueKind);
        Assert.AreEqual("hi", asStr.GetString());
    }

    // ─── Exclusions: generic / special-name ──────────────────────────────────

    [Test]
    public void AddRpcAllMethod_GenericMethod_NotRegistered()
    {
        _rpc.AddRpcAllMethod(typeof(Calc));
        Assert.Throws<KeyNotFoundException>(() => DispatchSync(Rpc(typeof(Calc), "Generic"), "{}", "1"));
    }

    [Test]
    public void AddRpcAllMethod_PropertyAccessor_NotRegistered()
    {
        _rpc.AddRpcAllMethod(typeof(Calc));
        Assert.Throws<KeyNotFoundException>(() => DispatchSync(Rpc(typeof(Calc), "get_Prop"), "{}"));
    }

    // ─── includeInherited ────────────────────────────────────────────────────

    [Test]
    public void AddRpcAllMethod_DeclaredOnly_ExcludesInherited()
    {
        _rpc.AddRpcAllMethod(typeof(Derived));
        var own = (JsonElement)DispatchSync(Rpc(typeof(Derived), "DerivedMethod"), "{}");
        Assert.AreEqual(2, own.GetInt32());
        Assert.Throws<KeyNotFoundException>(() => DispatchSync(Rpc(typeof(Derived), "BaseMethod"), "{}"));
    }

    [Test]
    public void AddRpcAllMethod_IncludeInherited_RegistersBaseMethod()
    {
        _rpc.AddRpcAllMethod(typeof(Derived), new RpcOptions { IncludeInherited = true });
        var inherited = (JsonElement)DispatchSync(Rpc(typeof(Derived), "BaseMethod"), "{}");
        Assert.AreEqual(1, inherited.GetInt32());
    }

    [Test]
    public void AddRpcAllMethod_IncludeInherited_ExcludesObjectMethods()
    {
        _rpc.AddRpcAllMethod(typeof(Derived), new RpcOptions { IncludeInherited = true });
        Assert.Throws<KeyNotFoundException>(() => DispatchSync(Rpc(typeof(Derived), "ToString"), "{}"));
    }

    // ─── AddRpcAllGetProperty (field + property via chain resolution) ─────────

    [Test]
    public void AddRpcAllGetProperty_FieldAndProperty_Resolve()
    {
        _rpc.AddRpcConverter(new FooConverter());
        _rpc.AddRpcAllGetProperty(typeof(Foo));

        var root = JsonDocument.Parse(
            @"{""rpcType"":""" + typeof(FooRpc).FullName + @""",""Value"":21}").RootElement;

        var field = _rpc.JsonRpc_ResolveChain(root, new RpcChainStep[0], "Value", default);
        Assert.AreEqual(21, field);

        var prop = _rpc.JsonRpc_ResolveChain(root, new RpcChainStep[0], "Doubled", default);
        Assert.AreEqual(42, prop);
    }

    // ─── AddRpcAllSetProperty (static field via chain resolution) ─────────────

    [Test]
    public void AddRpcAllSetProperty_StaticField_Assigns()
    {
        Foo.Counter = 0;
        _rpc.AddRpcConverter(new FooConverter());
        _rpc.AddRpcAllSetProperty(typeof(Foo));

        var root = JsonDocument.Parse(
            @"{""rpcType"":""" + typeof(FooRpc).FullName + @""",""Value"":0}").RootElement;
        var value = JsonDocument.Parse("99").RootElement;

        _rpc.JsonRpc_ResolveChainSet(root, new RpcChainStep[0], "Counter", value);
        Assert.AreEqual(99, Foo.Counter);
    }

    // ─── AddRpcAllMethod: nested types (includeNested) ────────────────────────

    [Test]
    public void AddRpcAllMethod_NestedDefault_NotRegistered()
    {
        _rpc.AddRpcAllMethod(typeof(Outer));
        Assert.Throws<KeyNotFoundException>(
            () => DispatchSync(Rpc(typeof(Outer.Inner), "InnerStatic"), "1"));
    }

    [Test]
    public void AddRpcAllMethod_IncludeNested_DispatchesNestedStatic()
    {
        _rpc.AddRpcAllMethod(typeof(Outer), new RpcOptions { IncludeNested = true });
        // Outer's own method is still registered.
        var outer = (JsonElement)DispatchSync(Rpc(typeof(Outer), "OuterMethod"), "{}");
        Assert.AreEqual(10, outer.GetInt32());
        // Nested type's static method is registered under the normalized name.
        var inner = (JsonElement)DispatchSync(Rpc(typeof(Outer.Inner), "InnerStatic"), "4");
        Assert.AreEqual(5, inner.GetInt32());
    }

    [Test]
    public void AddRpcAllMethod_IncludeNested_DispatchesDeeplyNested()
    {
        _rpc.AddRpcAllMethod(typeof(Outer), new RpcOptions { IncludeNested = true });
        var deep = (JsonElement)DispatchSync(Rpc(typeof(Outer.Inner.Deep), "DeepStatic"));
        Assert.AreEqual(99, deep.GetInt32());
    }

    [Test]
    public void AddRpcAllMethod_IncludeNested_FullNameUsesDotSeparator()
    {
        _rpc.AddRpcAllMethod(typeof(Outer), new RpcOptions { IncludeNested = true });
        // The CLR '+' form must not be registered; only the '.'-normalized name resolves.
        Assert.Throws<KeyNotFoundException>(
            () => DispatchSync(typeof(Outer.Inner).FullName + ".InnerStatic", "1"));
        var ok = (JsonElement)DispatchSync(Rpc(typeof(Outer.Inner), "InnerStatic"), "1");
        Assert.AreEqual(2, ok.GetInt32());
    }

    // ─── AddRpcAllGetProperty: nested type via chain resolution ───────────────

    [Test]
    public void AddRpcAllGetProperty_IncludeNested_ResolvesNestedMembers()
    {
        _rpc.AddRpcConverter(new ItemConverter());
        _rpc.AddRpcAllGetProperty(typeof(Container), new RpcOptions { IncludeNested = true });

        var root = JsonDocument.Parse(
            @"{""rpcType"":""" + typeof(ItemRpc).FullName + @""",""Value"":7}").RootElement;

        var field = _rpc.JsonRpc_ResolveChain(root, new RpcChainStep[0], "Value", default);
        Assert.AreEqual(7, field);

        var prop = _rpc.JsonRpc_ResolveChain(root, new RpcChainStep[0], "Tripled", default);
        Assert.AreEqual(21, prop);
    }

    // ─── DeserializeRoot: orgType resolves the concrete subtype ──────────────

    [Test]
    public void ResolveChain_OrgType_ResolvesConcreteSubtypeOverRegisteredBaseType()
    {
        var typeResolver = new TypeResolver(() => new UniLiquidLink.Server.NullLogger());
        typeResolver.RegisterAssembly(typeof(RpcRegistrarAllTests).Assembly);
        _rpc = new RpcRegistrar(_bus, _chain, () => new UniLiquidLink.Server.NullLogger(), typeResolver);
        _rpc.AddRpcConverter(new BaseConverter());
        _rpc.AddRpcAllDirectMethod(typeof(Derived));

        var root = JsonDocument.Parse(
            @"{""rpcType"":""" + typeof(BaseRpc).FullName + @""",""orgType"":""" + typeof(Derived).FullName + @"""}").RootElement;

        // DerivedMethod is only registered for Derived; if DeserializeRoot used the registered
        // (coarse) Base org type instead of resolving orgType, this would deserialize a Base
        // instance and the chain dispatch below would throw KeyNotFoundException.
        var result = _rpc.JsonRpc_ResolveChain(root, new RpcChainStep[0], "DerivedMethod", default);
        Assert.AreEqual(2, result);
    }

    // ─── SaveRpcNamesCsv: CSV contract ───────────────────────────────────────

    [Test]
    public void SaveRpcNamesCsv_NestedStatic_WritesSimpleClassName()
    {
        _rpc.AddRpcAllMethod(typeof(Outer), new RpcOptions { IncludeNested = true });
        string path = Path.GetTempFileName();
        try
        {
            _rpc.SaveRpcNamesCsv(path);
            string csv = File.ReadAllText(path);
            // The Python gateway collapses self.Inner.InnerStatic(...) via the (Inner, InnerStatic)
            // key, so the row must carry the dotted full name and the simple class_name "Inner".
            StringAssert.Contains(
                "RpcRegistrarAllTests.Outer.Inner.InnerStatic,Inner,InnerStatic", csv);
        }
        finally { File.Delete(path); }
    }

    // ─── AddRpcRootGetProperty (null-obj chain resolution) ───────────────────

    [Test]
    public void ResolveChain_NullObj_RootProperty_DirectTerminal()
    {
        var foo = new Foo { Value = 42 };
        _rpc.AddRpcRootGetProperty("MyFoo", () => foo);

        var nullRoot = JsonDocument.Parse("null").RootElement;
        var result = _rpc.JsonRpc_ResolveChain(
            nullRoot, new RpcChainStep[0], "MyFoo", default);
        Assert.AreEqual(foo, result);
    }

    [Test]
    public void ResolveChain_NullObj_ChainedStep_ResolvesField()
    {
        var foo = new Foo { Value = 7 };
        _rpc.AddRpcRootGetProperty("MyFoo", () => foo);
        _rpc.AddRpcGetProperty<Foo, int>(x => x.Value);

        var nullRoot = JsonDocument.Parse("null").RootElement;
        var steps = new[] { new RpcChainStep { name = "MyFoo" } };
        var result = _rpc.JsonRpc_ResolveChain(
            nullRoot, steps, "Value", default);
        Assert.AreEqual(7, result);
    }

    [Test]
    public void ResolveChain_NullObj_UnknownRootProperty_Throws()
    {
        var nullRoot = JsonDocument.Parse("null").RootElement;
        Assert.Throws<ArgumentException>(() =>
            _rpc.JsonRpc_ResolveChain(
                nullRoot, new RpcChainStep[0], "Nonexistent", default));
    }

    [Test]
    public void AddRpcRootGetProperty_Reregistration_UpdatesValue()
    {
        var first = new Foo { Value = 1 };
        var second = new Foo { Value = 2 };
        _rpc.AddRpcRootGetProperty("MyFoo", () => first);
        _rpc.AddRpcRootGetProperty("MyFoo", () => second);

        var nullRoot = JsonDocument.Parse("null").RootElement;
        var result = _rpc.JsonRpc_ResolveChain(
            nullRoot, new RpcChainStep[0], "MyFoo", default);
        Assert.AreEqual(second, result);
    }
}
