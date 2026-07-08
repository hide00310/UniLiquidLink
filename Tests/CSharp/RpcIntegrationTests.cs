using NUnit.Framework;
using System.IO;
using System.Text.Json;

[TestFixture]
public class RpcIntegrationTests
{
    RpcTestContext _ctx;

    [SetUp]
    public void SetUp()
    {
        SetupIntegrationTest.Setup();
        _ctx = new RpcTestContext();
    }

    public static string GoldenDir()
    {
        return Path.Combine(UniLiquidLinkTestHelper.GetSourceDir(), "GoldenFiles");
    }

    string ReadGolden(string name)
    {
        string path = Path.Combine(GoldenDir(), name + ".json");
        if (!File.Exists(path))
        {
            Assert.Inconclusive("Golden not found: " + path);
        }

        return File.ReadAllText(path).Trim();
    }

    void AssertMatchesGolden(string goldenName, string actual)
    {
        string normalized = RpcTestContext.NormalizeInstanceIds(actual);
        Assert.AreEqual(ReadGolden(goldenName), normalized.Trim());
    }

    // --- tests ---

    [Test]
    public void TestSampleMethodInt()
    {
        string resp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":1,""method"":""RpcTestContext.SampleMethodInt"",""params"":[42]}");
        AssertMatchesGolden("golden_sample_method_int", resp);
    }

    [Test]
    public void TestSampleVector3()
    {
        string resp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":2,""method"":""RpcTestContext.SampleVector3"",""params"":[{""x"":1,""y"":2,""z"":3}]}");
        AssertMatchesGolden("golden_sample_vector3", resp);
    }

    [Test]
    public void TestUnknownMethodError()
    {
        string resp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":3,""method"":""NoSuchMethod"",""params"":[]}");
        AssertMatchesGolden("golden_unknown_method_error", resp);
    }

    [Test]
    public void TestFind()
    {
        string resp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":4,""method"":""UnityEngine.GameObject.Find"",""params"":[""UniLiquidLinkTestObject""]}");
        AssertMatchesGolden("golden_find", resp);
    }

    [Test]
    public void TestResolveChainGetTransform()
    {
        // First get a live goRef (instanceId needed for registry lookup)
        string findResp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":4,""method"":""UnityEngine.GameObject.Find"",""params"":[""UniLiquidLinkTestObject""]}");
        string goRpcObj = JsonDocument.Parse(findResp).RootElement.GetProperty("result").GetRawText();

        string req = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":5,""method"":""JsonRpc_ResolveChain"",""params"":[{0},[],""transform"",null]}}",
            goRpcObj);
        string resp = _ctx.SendAndGetResponse(req);
        AssertMatchesGolden("golden_resolve_chain_get_transform", resp);
    }

    [Test]
    public void TestResolveChainChainedStep()
    {
        string findResp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":4,""method"":""UnityEngine.GameObject.Find"",""params"":[""UniLiquidLinkTestObject""]}");
        string goRpcObj = JsonDocument.Parse(findResp).RootElement.GetProperty("result").GetRawText();

        string req = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":6,""method"":""JsonRpc_ResolveChain"",""params"":[{0},[{{""name"":""transform""}}],""gameObject"",null]}}",
            goRpcObj);
        string resp = _ctx.SendAndGetResponse(req);
        AssertMatchesGolden("golden_resolve_chain_chained_step", resp);
    }

    [Test]
    public void TestResolveChainRotate()
    {
        string findResp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":4,""method"":""UnityEngine.GameObject.Find"",""params"":[""UniLiquidLinkTestObject""]}");
        string goRpcObj = JsonDocument.Parse(findResp).RootElement.GetProperty("result").GetRawText();

        string tReq = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":5,""method"":""JsonRpc_ResolveChain"",""params"":[{0},[],""transform"",null]}}",
            goRpcObj);
        string tResp = _ctx.SendAndGetResponse(tReq);
        string tRpcObj = JsonDocument.Parse(tResp).RootElement.GetProperty("result").GetRawText();

        string req = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":7,""method"":""JsonRpc_ResolveChain"",""params"":[{0},[],""Rotate"",[10.0,20.0,30.0,{{""value"":""Self"",""rpcEnum"":1}}]]}}",
            tRpcObj);
        string resp = _ctx.SendAndGetResponse(req);
        AssertMatchesGolden("golden_resolve_chain_rotate", resp);
    }

    [Test]
    public void TestResolveChainSetPosition()
    {
        string findResp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":4,""method"":""UnityEngine.GameObject.Find"",""params"":[""UniLiquidLinkTestObject""]}");
        string goRpcObj = JsonDocument.Parse(findResp).RootElement.GetProperty("result").GetRawText();

        // Set transform.position, then read it back to verify the assignment took effect.
        string setReq = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":8,""method"":""JsonRpc_ResolveChainSet"",""params"":[{0},[{{""name"":""transform""}}],""position"",{{""x"":1,""y"":2,""z"":3}}]}}",
            goRpcObj);
        _ctx.SendAndGetResponse(setReq);

        string getReq = string.Format(
            @"{{""jsonrpc"":""2.0"",""id"":9,""method"":""JsonRpc_ResolveChain"",""params"":[{0},[{{""name"":""transform""}}],""position"",null]}}",
            goRpcObj);
        string resp = _ctx.SendAndGetResponse(getReq);
        AssertMatchesGolden("golden_resolve_chain_set_position", resp);
    }
}
