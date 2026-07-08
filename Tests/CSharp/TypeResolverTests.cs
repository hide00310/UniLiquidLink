using LLiquidLink;
using NUnit.Framework;
using System;
using System.IO;
using UniLiquidLink;

[TestFixture]
public class TypeResolverTests
{
    TypeResolver _resolver;

    [SetUp]
    public void SetUp()
    {
        _resolver = new TypeResolver(() => new UniLiquidLink.Server.NullLogger());
        _resolver.RegisterAssembly(typeof(string).Assembly);
        _resolver.RegisterAssembly(typeof(UnityEngine.GameObject).Assembly);
    }

    [Test]
    public void Resolve_FullyQualifiedSystemType_String()
    {
        Assert.AreEqual(typeof(string), _resolver.Resolve("System.String"));
    }

    [Test]
    public void Resolve_FullyQualifiedSystemType_Int32()
    {
        Assert.AreEqual(typeof(int), _resolver.Resolve("System.Int32"));
    }

    [Test]
    public void Resolve_FullyQualifiedUnityType()
    {
        Assert.AreEqual(typeof(UnityEngine.GameObject), _resolver.Resolve("UnityEngine.GameObject"));
    }

    [Test]
    public void Resolve_ShortName_ThrowsTypeLoadException()
    {
        // Short names are no longer resolved in C#; the Python gateway handles expansion.
        Assert.Throws<TypeLoadException>(() => _resolver.Resolve("GameObject"));
    }

    [Test]
    public void Resolve_Unknown_ThrowsTypeLoadException()
    {
        Assert.Throws<TypeLoadException>(() => _resolver.Resolve("NoSuchType999AbcXyz"));
    }

    [Test]
    public void Resolve_AssemblyQualifiedName_ThrowsTypeLoadException()
    {
        Assert.Throws<TypeLoadException>(() => _resolver.Resolve("System.Int32, mscorlib"));
    }

    [Test]
    public void SaveAllowedTypesCsv_ContainsRegisteredTypes()
    {
        string path = Path.Combine(Path.GetTempPath(), "type_names_test.csv");
        try
        {
            _resolver.SaveAllowedTypesCsv(path);
            string csv = File.ReadAllText(path);
            Assert.IsTrue(csv.Contains("System.String"), "System.String should be in CSV");
            Assert.IsTrue(csv.Contains("UnityEngine.GameObject"), "UnityEngine.GameObject should be in CSV");
            Assert.IsTrue(csv.StartsWith("full_name"), "CSV should start with header");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
