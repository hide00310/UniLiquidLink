using LLiquidLink;
using NUnit.Framework;
using System.Collections.Generic;

[TestFixture]
public class RpcTimingStatsTests
{
    static Dictionary<string, object> ZeroDict()
    {
        return new Dictionary<string, object>
        {
            ["rpc_count"] = 0L,
            ["local_processing_time"] = 0.0,
            ["communication_time"] = 0.0,
            ["remote_time"] = 0.0,
            ["total_time"] = 0.0,
            ["response_network_time"] = 0.0,
            ["request_network_time"] = 0.0,
            ["bytes_sent"] = 0L,
            ["bytes_received"] = 0L,
            ["dispatch_queue_time"] = 0.0,
            ["decode_time"] = 0.0,
            ["resolve_time"] = 0.0,
            ["deserialize_args_time"] = 0.0,
            ["invoke_time"] = 0.0,
            ["serialize_result_time"] = 0.0,
            ["encode_time"] = 0.0,
            ["send_time"] = 0.0,
            ["overload_attempt_count"] = 0L,
        };
    }

    [Test]
    public void DisabledByDefault_RecordIsNoop()
    {
        var stats = new RpcTimingStats();
        stats.Record(localProcessingTime: 1.0, remoteTime: 1.0);
        CollectionAssert.AreEquivalent(ZeroDict(), stats.ToDict());
    }

    [Test]
    public void Enabled_RecordAccumulates()
    {
        var stats = new RpcTimingStats(enabled: true);
        stats.Record(localProcessingTime: 0.5, remoteTime: 0.25);
        stats.Record(localProcessingTime: 0.5, remoteTime: 0.25);
        var d = stats.ToDict();
        Assert.AreEqual(2L, d["rpc_count"]);
        Assert.AreEqual(1.0, d["local_processing_time"]);
        Assert.AreEqual(0.5, d["remote_time"]);
        Assert.AreEqual(1.5, d["total_time"]);
    }

    [Test]
    public void CommunicationTime_IsDerivedFromNetworkTimeFields()
    {
        var stats = new RpcTimingStats(enabled: true);
        stats.Record(localProcessingTime: 0.5, requestNetworkTime: 0.1, responseNetworkTime: 0.2);
        stats.Record(localProcessingTime: 0.5, requestNetworkTime: 0.4, responseNetworkTime: 0.3);
        var d = stats.ToDict();
        Assert.AreEqual(0.5, (double)d["request_network_time"], 1e-9);
        Assert.AreEqual(0.5, (double)d["response_network_time"], 1e-9);
        Assert.AreEqual((double)d["request_network_time"] + (double)d["response_network_time"],
            (double)d["communication_time"], 1e-9);
    }

    [Test]
    public void Record_NetworkTimeFields_DefaultToZeroWhenNotGiven()
    {
        var stats = new RpcTimingStats(enabled: true);
        stats.Record(localProcessingTime: 1.0);
        var d = stats.ToDict();
        Assert.AreEqual(0.0, d["request_network_time"]);
        Assert.AreEqual(0.0, d["response_network_time"]);
        Assert.AreEqual(0.0, d["remote_time"]);
        Assert.AreEqual(0.0, d["communication_time"]);
    }

    [Test]
    public void TotalTime_EqualsLocalPlusCommunicationPlusRemote()
    {
        var stats = new RpcTimingStats(enabled: true);
        stats.Record(localProcessingTime: 1.0, remoteTime: 2.0, requestNetworkTime: 0.3, responseNetworkTime: 0.4);
        var d = stats.ToDict();
        Assert.AreEqual((double)d["local_processing_time"] + (double)d["communication_time"] + (double)d["remote_time"],
            (double)d["total_time"], 1e-9);
    }

    [Test]
    public void RecordBytes_DisabledByDefault_IsNoop()
    {
        var stats = new RpcTimingStats();
        stats.RecordBytes(sent: 100, received: 200);
        var d = stats.ToDict();
        Assert.AreEqual(0L, d["bytes_sent"]);
        Assert.AreEqual(0L, d["bytes_received"]);
    }

    [Test]
    public void RecordBytes_Enabled_AccumulatesSentAndReceivedIndependently()
    {
        var stats = new RpcTimingStats(enabled: true);
        stats.RecordBytes(sent: 100);
        stats.RecordBytes(received: 50);
        stats.RecordBytes(sent: 10, received: 20);
        var d = stats.ToDict();
        Assert.AreEqual(110L, d["bytes_sent"]);
        Assert.AreEqual(70L, d["bytes_received"]);
    }

    [Test]
    public void RecordBytes_DoesNotAffectRpcCount()
    {
        var stats = new RpcTimingStats(enabled: true);
        stats.RecordBytes(sent: 100, received: 100);
        stats.RecordBytes(sent: 100, received: 100);
        Assert.AreEqual(0L, stats.ToDict()["rpc_count"]);
    }

    [Test]
    public void RecordStages_DisabledByDefault_IsNoop()
    {
        var stats = new RpcTimingStats();
        var stages = new RpcStageTimings
        {
            ResolveTime = 1.0,
            DeserializeArgsTime = 1.0,
            InvokeTime = 1.0,
            SerializeResultTime = 1.0,
            OverloadAttempts = 3,
        };
        stats.RecordStages(stages, dispatchQueueTime: 1.0, decodeTime: 1.0, encodeTime: 1.0, sendTime: 1.0);
        CollectionAssert.AreEquivalent(ZeroDict(), stats.ToDict());
    }

    [Test]
    public void RecordStages_Enabled_AccumulatesAllNineKeys()
    {
        var stats = new RpcTimingStats(enabled: true);
        var stages1 = new RpcStageTimings { ResolveTime = 0.1, DeserializeArgsTime = 0.2, InvokeTime = 0.3, SerializeResultTime = 0.4, OverloadAttempts = 1 };
        var stages2 = new RpcStageTimings { ResolveTime = 0.1, DeserializeArgsTime = 0.2, InvokeTime = 0.3, SerializeResultTime = 0.4, OverloadAttempts = 2 };
        stats.RecordStages(stages1, dispatchQueueTime: 0.5, decodeTime: 0.6, encodeTime: 0.7, sendTime: 0.8);
        stats.RecordStages(stages2, dispatchQueueTime: 0.5, decodeTime: 0.6, encodeTime: 0.7, sendTime: 0.8);
        var d = stats.ToDict();
        Assert.AreEqual(1.0, (double)d["dispatch_queue_time"], 1e-9);
        Assert.AreEqual(1.2, (double)d["decode_time"], 1e-9);
        Assert.AreEqual(0.2, (double)d["resolve_time"], 1e-9);
        Assert.AreEqual(0.4, (double)d["deserialize_args_time"], 1e-9);
        Assert.AreEqual(0.6, (double)d["invoke_time"], 1e-9);
        Assert.AreEqual(0.8, (double)d["serialize_result_time"], 1e-9);
        Assert.AreEqual(1.4, (double)d["encode_time"], 1e-9);
        Assert.AreEqual(1.6, (double)d["send_time"], 1e-9);
        Assert.AreEqual(3L, d["overload_attempt_count"]);
        // RecordStages is purely additive and does not own rpc_count.
        Assert.AreEqual(0L, d["rpc_count"]);
    }

    [Test]
    public void Reset_ClearsTotals_ButKeepsEnabled()
    {
        var stats = new RpcTimingStats(enabled: true);
        stats.Record(localProcessingTime: 1.0, remoteTime: 0.5, requestNetworkTime: 0.3, responseNetworkTime: 0.2);
        stats.RecordBytes(sent: 100, received: 200);
        var stages = new RpcStageTimings { ResolveTime = 1.0, DeserializeArgsTime = 1.0, InvokeTime = 1.0, SerializeResultTime = 1.0, OverloadAttempts = 1 };
        stats.RecordStages(stages, dispatchQueueTime: 1.0, decodeTime: 1.0, encodeTime: 1.0, sendTime: 1.0);
        stats.Reset();
        Assert.IsTrue(stats.Enabled);
        CollectionAssert.AreEquivalent(ZeroDict(), stats.ToDict());
    }
}
