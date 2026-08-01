using LLiquidLink;
using NUnit.Framework;
using System.IO;

[TestFixture]
public class UtilsTests
{
    [Test]
    public void ResolveDataDir_RelativePath_ReturnsRootedPath()
    {
        string result = Utils.ResolveDataDir("SomeRelative/ServerDir");
        Assert.IsTrue(Path.IsPathRooted(result));
    }
}
