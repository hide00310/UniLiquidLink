using LLiquidLink;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using UniLiquidLink;

[TestFixture]
public class StdioTransportTests
{
    ConcurrentQueue<Action> _queue;
    StdioTransport _transport;

    [SetUp]
    public void SetUp()
    {
        var chain = new JsonSerializerChain();
        var rpc = new RpcRegistry(() => new UniLiquidLink.Server.NullLogger());
        var caller = new MethodCaller(() => new UniLiquidLink.Server.NullLogger(), chain, rpc.Searcher);
        rpc.Register("echo", (Func<int, int>)(x => x));
        rpc.Register("noop", (Action)(() => { }));
        // Deliberately slow handler: proves echoSentAt is timestamped AFTER invoke completes,
        // not before, and gives invoke_time a floor that cannot round down to 0.
        rpc.Register("slow", (Func<int, int>)(x => { Thread.Sleep(50); return x; }));

        _queue = new ConcurrentQueue<Action>();
        var dispatcher = new StubDispatcher(_queue);
        _transport = new StdioTransport(dispatcher, caller, chain.Options[(int)JsonSerializerChain.Stage.Main], () => new UniLiquidLink.Server.NullLogger());
    }

    [TearDown]
    public void TearDown()
    {
        _transport.Stop();
    }

    static byte[] Frame(string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[4 + body.Length];
        BitConverter.GetBytes(body.Length).CopyTo(frame, 0);
        body.CopyTo(frame, 4);
        return frame;
    }

    // Drains the dispatcher queue on this (test) thread, standing in for the Unity main thread,
    // until at least one response frame has been written to `output` or the timeout elapses.
    void RunUntilResponse(MemoryStream output)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (_queue.TryDequeue(out Action action))
            {
                action();
                if (output.Length > 0)
                {
                    return;
                }
            }
            else
            {
                Thread.Sleep(5);
            }
        }
        Assert.Fail("Timed out waiting for StdioTransport to dispatch a response.");
    }

    static JsonElement ParseBody(MemoryStream stream)
    {
        byte[] bytes = stream.ToArray();
        return JsonDocument.Parse(bytes.AsMemory(4)).RootElement.Clone();
    }

    [Test]
    public void Dispatch_RequestWithSentAt_EchoesSentAtAndRecordsRequestNetworkTime()
    {
        var input = new MemoryStream(Frame(@"{""jsonrpc"":""2.0"",""id"":1,""method"":""echo"",""params"":[42],""sent_at"":100.0}"));
        var output = new MemoryStream();
        _transport.AttachStreams(input, output, new MemoryStream());
        _transport.Start();

        RunUntilResponse(output);

        JsonElement resp = ParseBody(output);
        Assert.IsTrue(resp.TryGetProperty("sent_at", out _), "Response must echo back 'sent_at' when the request carried one.");
        Assert.IsTrue(resp.TryGetProperty("received_at", out _), "Response must echo back 'received_at' when the request carried a 'sent_at'.");

        var d = _transport.Timing.ToDict();
        Assert.AreEqual(1L, d["rpc_count"]);
        Assert.Greater((double)d["request_network_time"], 0.0);
        Assert.AreEqual((long)input.Length, d["bytes_received"]);
        Assert.Greater((long)d["bytes_sent"], 0L);
    }

    [Test]
    public void Dispatch_RequestWithoutSentAt_NoEchoAndZeroRequestNetworkTime()
    {
        var input = new MemoryStream(Frame(@"{""jsonrpc"":""2.0"",""id"":1,""method"":""echo"",""params"":[42]}"));
        var output = new MemoryStream();
        _transport.AttachStreams(input, output, new MemoryStream());
        _transport.Start();

        RunUntilResponse(output);

        JsonElement resp = ParseBody(output);
        Assert.IsFalse(resp.TryGetProperty("sent_at", out _), "Response must not gain a 'sent_at' the request never sent.");
        Assert.IsFalse(resp.TryGetProperty("received_at", out _), "Response must not gain a 'received_at' the request never asked for.");
        Assert.AreEqual(0.0, _transport.Timing.ToDict()["request_network_time"]);
    }

    [Test]
    public void Dispatch_HandlerDelay_SentAtIsTimestampedAfterInvoke()
    {
        var input = new MemoryStream(Frame(@"{""jsonrpc"":""2.0"",""id"":1,""method"":""slow"",""params"":[42],""sent_at"":100.0}"));
        var output = new MemoryStream();
        _transport.AttachStreams(input, output, new MemoryStream());
        _transport.Start();

        RunUntilResponse(output);

        JsonElement resp = ParseBody(output);
        double receivedAt = resp.GetProperty("received_at").GetDouble();
        double sentAt = resp.GetProperty("sent_at").GetDouble();
        // received_at is stamped before invoke (in ReadLoop/Dispatch entry), sent_at is stamped
        // after invoke completes (pre-encode). The "slow" handler's deliberate 50ms sleep must
        // therefore show up in their gap, proving invoke time no longer leaks into the sender's
        // measured response_network_time (which is derived from sent_at onward).
        Assert.GreaterOrEqual(sentAt - receivedAt, 0.04);
    }

    [Test]
    public void Dispatch_RecordsStageBreakdown()
    {
        var input = new MemoryStream(Frame(@"{""jsonrpc"":""2.0"",""id"":1,""method"":""slow"",""params"":[42],""sent_at"":100.0}"));
        var output = new MemoryStream();
        _transport.AttachStreams(input, output, new MemoryStream());
        _transport.Start();

        RunUntilResponse(output);

        var d = _transport.Timing.ToDict();
        // invoke_time and dispatch_queue_time have a real floor (the handler's 50ms sleep, and
        // the polling loop's Thread.Sleep(5) waits in RunUntilResponse) so they cannot round to 0.
        Assert.GreaterOrEqual((double)d["invoke_time"], 0.04);
        Assert.Greater((double)d["dispatch_queue_time"], 0.0);
        // The remaining stages are real but too fast to floor deterministically; only check they
        // were touched at all (non-negative, i.e. RecordStages ran rather than being skipped).
        Assert.GreaterOrEqual((double)d["decode_time"], 0.0);
        Assert.GreaterOrEqual((double)d["resolve_time"], 0.0);
        Assert.GreaterOrEqual((double)d["deserialize_args_time"], 0.0);
        Assert.GreaterOrEqual((double)d["serialize_result_time"], 0.0);
        Assert.GreaterOrEqual((double)d["encode_time"], 0.0);
        Assert.GreaterOrEqual((double)d["send_time"], 0.0);
        Assert.AreEqual(1L, d["overload_attempt_count"]);
    }

    [Test]
    public void Dispatch_Notification_RecordsBytesButNotRpcCount()
    {
        byte[] req = Frame(@"{""jsonrpc"":""2.0"",""method"":""noop"",""params"":[]}");
        var input = new MemoryStream(req);
        var output = new MemoryStream();
        _transport.AttachStreams(input, output, new MemoryStream());
        _transport.Start();

        // Notifications get no response frame; wait for the byte count to reflect the dispatch instead.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && (long)_transport.Timing.ToDict()["bytes_received"] == 0)
        {
            if (_queue.TryDequeue(out Action action))
            {
                action();
            }
            else
            {
                Thread.Sleep(5);
            }
        }

        var d = _transport.Timing.ToDict();
        Assert.AreEqual(0L, d["rpc_count"]);
        Assert.AreEqual((long)req.Length, d["bytes_received"]);
    }
}
