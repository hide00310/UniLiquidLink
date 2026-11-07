using System.Collections.Generic;

namespace LLiquidLink
{
    /// <summary>
    /// Tracks one stdio connection's RPC timing: request count, the local/communication/remote
    /// breakdown of total_time, wall-clock network transit times, per-stage breakdown of local
    /// processing, and wire byte volume. Mirrors the Python-side <c>RpcTimingStats</c>
    /// (<c>Python~/lliquidlink/core/_timing.py</c>) for the gateway&lt;-&gt;Unity IPC leg; see that
    /// module's docstring for the shared <c>total_time = local_processing_time +
    /// communication_time + remote_time</c> model and the clock-skew cancellation argument.
    /// C# only ever plays the JSON-RPC server role on this leg (it never issues its own outbound
    /// request and awaits a reply), so <see cref="RemoteTime"/> is always 0.0 and
    /// <see cref="CommunicationTime"/> (derived, like the Python side) always equals
    /// <see cref="RequestNetworkTime"/>.
    /// <see cref="BytesSent"/>/<see cref="BytesReceived"/> count every frame that crosses the wire,
    /// including notifications, so they do not correlate 1:1 with <see cref="RpcCount"/>.
    /// A single instance is only ever touched from the main thread inside the transport's dispatch
    /// method (the "single event loop" equivalent of the Python design); it is not safe to share
    /// across threads.
    /// </summary>
    public class RpcTimingStats
    {
        /// <summary>When false, <see cref="Record"/>/<see cref="RecordStages"/>/<see cref="RecordBytes"/> are no-ops.</summary>
        public bool Enabled { get; }

        long _rpcCount;
        double _localProcessingTime;
        double _remoteTime;
        double _responseNetworkTime;
        double _requestNetworkTime;
        long _bytesSent;
        long _bytesReceived;

        // Per-stage breakdown of local processing (see RecordStages). dispatchQueueTime is NOT
        // part of localProcessingTime: it covers the wait before the main-thread Stopwatch even
        // starts (see StdioTransport.ReadLoop/Dispatch), so it is tracked separately here.
        double _dispatchQueueTime;
        double _decodeTime;
        double _resolveTime;
        double _deserializeArgsTime;
        double _invokeTime;
        double _serializeResultTime;
        double _encodeTime;
        double _sendTime;
        long _overloadAttemptCount;

        /// <summary>Create a stats tracker, initially with all counters at zero.</summary>
        /// <param name="enabled">When false, all recording methods are no-ops.</param>
        public RpcTimingStats(bool enabled = false)
        {
            Enabled = enabled;
        }

        /// <summary>
        /// Add one completed request/response cycle's timing to the running totals; no-op when disabled.
        /// </summary>
        /// <param name="localProcessingTime">Local processing time for this request (deserialize + handler invocation + response framing).</param>
        /// <param name="remoteTime">Time the peer spent processing this request; always 0.0 on this (server-only) leg.</param>
        /// <param name="responseNetworkTime">Wire transit time of the response this peer sent; always 0.0 on this (server-only) leg.</param>
        /// <param name="requestNetworkTime">Wire transit time of this request, or 0.0 if it carried no "sent_at".</param>
        public void Record(double localProcessingTime, double remoteTime = 0.0, double responseNetworkTime = 0.0, double requestNetworkTime = 0.0)
        {
            if (!Enabled)
            {
                return;
            }
            _rpcCount++;
            _localProcessingTime += localProcessingTime;
            _remoteTime += remoteTime;
            _responseNetworkTime += responseNetworkTime;
            _requestNetworkTime += requestNetworkTime;
        }

        /// <summary>
        /// Add one request's stage breakdown to the running totals; no-op when disabled. Additive
        /// only (does not touch <see cref="RpcCount"/>, which <see cref="Record"/> owns).
        /// </summary>
        /// <param name="stages">Per-call stage timings gathered by <see cref="MethodCaller.Call(string, System.Text.Json.JsonElement[], RpcStageTimings)"/>.</param>
        /// <param name="dispatchQueueTime">Wait between the request being fully received and being picked up on the main thread; NOT part of <see cref="LocalProcessingTime"/> (see class docs).</param>
        /// <param name="decodeTime">Time spent deserializing the request frame into an <c>RpcRequest</c>.</param>
        /// <param name="encodeTime">Time spent building the response frame.</param>
        /// <param name="sendTime">Time spent writing and flushing the response frame.</param>
        public void RecordStages(RpcStageTimings stages, double dispatchQueueTime, double decodeTime, double encodeTime, double sendTime)
        {
            if (!Enabled)
            {
                return;
            }
            _dispatchQueueTime += dispatchQueueTime;
            _decodeTime += decodeTime;
            _resolveTime += stages.ResolveTime;
            _deserializeArgsTime += stages.DeserializeArgsTime;
            _invokeTime += stages.InvokeTime;
            _serializeResultTime += stages.SerializeResultTime;
            _encodeTime += encodeTime;
            _sendTime += sendTime;
            _overloadAttemptCount += stages.OverloadAttempts;
        }

        /// <summary>
        /// Add wire byte counts to the running totals; no-op when disabled. Does not touch
        /// <see cref="RpcCount"/>: called for every frame on the wire (including notifications), not
        /// just completed request/response cycles.
        /// </summary>
        public void RecordBytes(long sent = 0, long received = 0)
        {
            if (!Enabled)
            {
                return;
            }
            _bytesSent += sent;
            _bytesReceived += received;
        }

        /// <summary>Clear accumulated totals for a new connection; keeps <see cref="Enabled"/>.</summary>
        public void Reset()
        {
            _rpcCount = 0;
            _localProcessingTime = 0.0;
            _remoteTime = 0.0;
            _responseNetworkTime = 0.0;
            _requestNetworkTime = 0.0;
            _bytesSent = 0;
            _bytesReceived = 0;
            _dispatchQueueTime = 0.0;
            _decodeTime = 0.0;
            _resolveTime = 0.0;
            _deserializeArgsTime = 0.0;
            _invokeTime = 0.0;
            _serializeResultTime = 0.0;
            _encodeTime = 0.0;
            _sendTime = 0.0;
            _overloadAttemptCount = 0;
        }

        /// <summary>
        /// Return the current stats as a plain dict, key-compatible with the Python side's
        /// <c>to_dict()</c>, plus this leg's stage breakdown. <c>communication_time</c> is derived
        /// (<c>request_network_time + response_network_time</c>), not stored, matching the Python side.
        /// </summary>
        public Dictionary<string, object> ToDict()
        {
            double communicationTime = _requestNetworkTime + _responseNetworkTime;
            return new Dictionary<string, object>
            {
                ["rpc_count"] = _rpcCount,
                ["local_processing_time"] = _localProcessingTime,
                ["communication_time"] = communicationTime,
                ["remote_time"] = _remoteTime,
                ["total_time"] = _localProcessingTime + communicationTime + _remoteTime,
                ["response_network_time"] = _responseNetworkTime,
                ["request_network_time"] = _requestNetworkTime,
                ["bytes_sent"] = _bytesSent,
                ["bytes_received"] = _bytesReceived,
                ["dispatch_queue_time"] = _dispatchQueueTime,
                ["decode_time"] = _decodeTime,
                ["resolve_time"] = _resolveTime,
                ["deserialize_args_time"] = _deserializeArgsTime,
                ["invoke_time"] = _invokeTime,
                ["serialize_result_time"] = _serializeResultTime,
                ["encode_time"] = _encodeTime,
                ["send_time"] = _sendTime,
                ["overload_attempt_count"] = _overloadAttemptCount,
            };
        }
    }
}
