using LLiquidLink;
using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;

[TestFixture]
public class UtilsTests
{
    [Test]
    public void ResolveDataDir_RelativePath_ReturnsRootedPath()
    {
        string result = Utils.ResolveDataDir("SomeRelative/ServerDir");
        Assert.IsTrue(Path.IsPathRooted(result));
    }

    [Test]
    public void UnwrapTargetInvocation_WrappedException_ReturnsInnerException()
    {
        var inner = new InvalidOperationException("boom");
        var wrapped = new TargetInvocationException(inner);
        Assert.AreSame(inner, Utils.UnwrapTargetInvocation(wrapped));
    }

    [Test]
    public void UnwrapTargetInvocation_UnwrappedException_ReturnsSameException()
    {
        var ex = new InvalidOperationException("boom");
        Assert.AreSame(ex, Utils.UnwrapTargetInvocation(ex));
    }
}
