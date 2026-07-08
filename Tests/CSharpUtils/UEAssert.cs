using NUnit.Framework;

/// <summary>
/// Assert wrapper that logs expected/actual values to the Test Console before delegating to NUnit Assert.
/// </summary>
public static class UEAssert
{
    public static void AreEqual(object expected, object actual)
    {
        TestContext.WriteLine($"[Assert] AreEqual expected={Fmt(expected)} actual={Fmt(actual)}");
        Assert.AreEqual(expected, actual);
    }

    public static void AreEqual(object expected, object actual, string message)
    {
        TestContext.WriteLine($"[Assert] AreEqual expected={Fmt(expected)} actual={Fmt(actual)}");
        Assert.AreEqual(expected, actual, message);
    }

    public static void AreEqual(double expected, double actual, double delta)
    {
        TestContext.WriteLine($"[Assert] AreEqual expected={expected} actual={actual} delta={delta}");
        Assert.AreEqual(expected, actual, delta);
    }

    public static void IsTrue(bool condition)
    {
        TestContext.WriteLine($"[Assert] IsTrue: {condition}");
        Assert.IsTrue(condition);
    }

    public static void IsTrue(bool condition, string message)
    {
        TestContext.WriteLine($"[Assert] IsTrue: {condition}");
        Assert.IsTrue(condition, message);
    }

    public static void IsFalse(bool condition)
    {
        TestContext.WriteLine($"[Assert] IsFalse: {condition}");
        Assert.IsFalse(condition);
    }

    public static void IsFalse(bool condition, string message)
    {
        TestContext.WriteLine($"[Assert] IsFalse: {condition}");
        Assert.IsFalse(condition, message);
    }

    public static void IsNotNull(object obj)
    {
        TestContext.WriteLine($"[Assert] IsNotNull: {Fmt(obj)}");
        Assert.IsNotNull(obj);
    }

    public static void IsNotNull(object obj, string message)
    {
        TestContext.WriteLine($"[Assert] IsNotNull: {Fmt(obj)}");
        Assert.IsNotNull(obj, message);
    }

    public static void IsNull(object obj)
    {
        TestContext.WriteLine($"[Assert] IsNull: {Fmt(obj)}");
        Assert.IsNull(obj);
    }

    public static void AreSame(object expected, object actual)
    {
        TestContext.WriteLine($"[Assert] AreSame expected={Fmt(expected)} actual={Fmt(actual)}");
        Assert.AreSame(expected, actual);
    }

    public static void StringContains(string expected, string actual)
    {
        TestContext.WriteLine($"[Assert] StringContains expected=\"{expected}\" in actual=\"{actual}\"");
        StringAssert.Contains(expected, actual);
    }

    private static string Fmt(object obj)
    {
        return obj != null ? obj.ToString() : "null";
    }
}
