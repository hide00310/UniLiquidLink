using NUnit.Framework;
using System.Text.Json;

[TestFixture]
public class JsonRpcProtocolTests
{
    RpcTestContext _ctx;

    [SetUp]
    public void SetUp()
    {
        SetupIntegrationTest.Setup();
        _ctx = new RpcTestContext();
    }

    [Test]
    public void HandleMessage_Success_ReturnsJsonRpc20WithResult()
    {
        string resp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":1,""method"":""RpcTestContext.SampleMethodInt"",""params"":[42]}");
        var doc = JsonDocument.Parse(resp).RootElement;
        Assert.AreEqual("2.0", doc.GetProperty("jsonrpc").GetString());
        Assert.AreEqual(1, doc.GetProperty("id").GetInt32());
        Assert.IsTrue(doc.TryGetProperty("result", out _), "Successful response must have 'result'");
        Assert.IsFalse(doc.TryGetProperty("error", out _), "Successful response must not have 'error'");
    }

    [Test]
    public void HandleMessage_UnknownMethod_ReturnsJsonRpcErrorCode()
    {
        string resp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":2,""method"":""NoSuchMethod999"",""params"":[]}");
        var doc = JsonDocument.Parse(resp).RootElement;
        Assert.AreEqual("2.0", doc.GetProperty("jsonrpc").GetString());
        Assert.AreEqual(2, doc.GetProperty("id").GetInt32());
        Assert.IsTrue(doc.TryGetProperty("error", out var err), "Error response must have 'error'");
        Assert.AreEqual(-32603, err.GetProperty("code").GetInt32());
        Assert.IsTrue(err.TryGetProperty("message", out _), "Error must have 'message'");
        Assert.IsFalse(doc.TryGetProperty("result", out _), "Error response must not have 'result'");
    }

    [Test]
    public void HandleMessage_IdPreserved_InBothSuccessAndError()
    {
        string succResp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":99,""method"":""RpcTestContext.SampleMethodInt"",""params"":[1]}");
        Assert.AreEqual(99, JsonDocument.Parse(succResp).RootElement.GetProperty("id").GetInt32());

        string errResp = _ctx.SendAndGetResponse(
            @"{""jsonrpc"":""2.0"",""id"":88,""method"":""Missing"",""params"":[]}");
        Assert.AreEqual(88, JsonDocument.Parse(errResp).RootElement.GetProperty("id").GetInt32());
    }
}
