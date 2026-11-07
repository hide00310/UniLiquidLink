using LLiquidLink.Logger;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace LLiquidLink
{
    /// <summary>
    /// Reads JSON-RPC requests from the Python middleware's stdio streams with 4-byte little-endian
    /// length framing, dispatches via <see cref="MethodCaller"/>, and writes responses.
    /// </summary>
    public class StdioTransport : ITransportServer
    {
        const string Endpoint = "stdio";

        readonly IMainThreadDispatcher _dispatcher;
        readonly MethodCaller _caller;
        readonly JsonSerializerOptions _jsonOptions;
        readonly Func<ILogger> _getLogger;
        readonly object _sendLock = new object();

        Stream _in;
        Stream _out;
        Stream _err;
        Thread _readThread;
        Thread _errReadThread;
        volatile bool _running;
        bool _streamsAttached;

        readonly RpcTimingStats _timing = new RpcTimingStats(enabled: true);

        /// <summary>RPC timing/byte-volume stats for this stdio connection (gateway leg of the IPC hop).</summary>
        public RpcTimingStats Timing => _timing;

        /// <inheritdoc/>
        public int ClientId { get; private set; }

        /// <inheritdoc/>
        public event Action<int, string> OnConnect;

        /// <inheritdoc/>
        public event Action<int> OnDisconnect;

        /// <inheritdoc/>
        public event Action<int, ArraySegment<byte>> OnData;

        /// <inheritdoc/>
        public event Action<int, Exception> OnError;

        /// <summary>Initialize StdioTransport with its dependencies. Call <see cref="AttachStreams"/> before <see cref="Start"/>.</summary>
        public StdioTransport(
            IMainThreadDispatcher dispatcher,
            MethodCaller caller,
            JsonSerializerOptions jsonOptions,
            Func<ILogger> getLogger)
        {
            _dispatcher = dispatcher;
            _caller = caller;
            _jsonOptions = jsonOptions;
            _getLogger = getLogger;
        }

        /// <summary>Attach the Python middleware's stdio streams. Must be called once before <see cref="Start"/>.</summary>
        /// <param name="inStream">Stream to read requests from (Python middleware's stdout).</param>
        /// <param name="outStream">Stream to write responses to (Python middleware's stdin).</param>
        /// <param name="errStream">Stream to read the Python middleware's stderr from.</param>
        public void AttachStreams(Stream inStream, Stream outStream, Stream errStream)
        {
            _in = inStream;
            _out = outStream;
            _err = errStream;
            _streamsAttached = true;
        }

        /// <summary>Start the read loop on a background thread and fire <see cref="OnConnect"/>.</summary>
        /// <exception cref="InvalidOperationException">Thrown when <see cref="AttachStreams"/> was not called first.</exception>
        public void Start()
        {
            if (!_streamsAttached)
            {
                throw new InvalidOperationException("AttachStreams must be called before Start().");
            }
            ClientId = 1;
            // Scope _timing to this Python child process's lifetime (Start() is 1:1 with it), so it
            // matches the "ipc" leg's scope rather than accumulating across the whole Editor session.
            _timing.Reset();
            _running = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "StdioTransport" };
            _readThread.Start();
            _errReadThread = new Thread(ErrorReadLoop) { IsBackground = true, Name = "StdioTransport-Err" };
            _errReadThread.Start();
            _dispatcher.Enqueue(() => OnConnect?.Invoke(ClientId, Endpoint));
        }

        /// <summary>
        /// Signal the read loop to stop and release its thread/stream resources. Closing the streams
        /// unblocks the background threads' blocking reads so they can exit and be joined.
        /// </summary>
        public void Stop()
        {
            if (!_running)
            {
                return;
            }
            _running = false;

            try { _in?.Close(); } catch { }
            try { _err?.Close(); } catch { }

            _readThread?.Join(2000);
            _errReadThread?.Join(2000);
        }

        /// <summary>Write <paramref name="data"/> to the Python middleware's stdin (there is only ever one stdio client).</summary>
        public void SendAll(ArraySegment<byte> data)
        {
            Send(data.Array, data.Offset, data.Count);
        }

        // ── Framing ──────────────────────────────────────────────────────────────

        void Send(byte[] frame)
        {
            Send(frame, 0, frame.Length);
        }

        void Send(byte[] buffer, int offset, int count)
        {
            lock (_sendLock)
            {
                _out.Write(buffer, offset, count);
                _out.Flush();
            }
        }

        // ── Background read thread ────────────────────────────────────────────────

        void ReadLoop()
        {
            try
            {
                while (_running)
                {
                    int contentLength = JsonRpcFraming.ReadFrameLength(_in);
                    if (contentLength < 0)
                    {
                        break;
                    }

                    byte[] body = new byte[contentLength];
                    if (!ReadFully(body))
                    {
                        break;
                    }

                    byte[] captured = body;
                    double receivedWall = NowUnixSeconds();
                    int frameBytes = 4 + contentLength;
                    // Stopwatch.GetTimestamp() is monotonic across the whole process, so a difference
                    // taken later on the main thread is a valid elapsed measurement despite the thread hop.
                    long enqueuedTicks = Stopwatch.GetTimestamp();
                    _dispatcher.Enqueue(() => Dispatch(captured, receivedWall, frameBytes, enqueuedTicks));
                }
            }
            catch (Exception ex)
            {
                // If _running is already false, Stop() closed the streams deliberately to unblock
                // this read; that is expected shutdown noise, not a real transport error.
                if (_running)
                {
                    _getLogger().Info("StdioTransport read error: " + ex.Message);
                    _dispatcher.Enqueue(() => OnError?.Invoke(ClientId, ex));
                }
            }

            if (_running)
            {
                _running = false;
                _dispatcher.Enqueue(() => OnDisconnect?.Invoke(ClientId));
            }
        }

        void ErrorReadLoop()
        {
            try
            {
                string text;
                using (var reader = new StreamReader(_err))
                {
                    text = reader.ReadToEnd();
                }

                if (!string.IsNullOrEmpty(text))
                {
                    _getLogger().Info("StdioTransport stderr: " + text);
                    var ex = new Exception(text);
                    _dispatcher.Enqueue(() => OnError?.Invoke(ClientId, ex));
                }
            }
            catch (Exception ex)
            {
                // See ReadLoop: a deliberate Stop() closes _err to unblock this read.
                if (_running)
                {
                    _getLogger().Info("StdioTransport stderr read error: " + ex.Message);
                }
            }
        }

        bool ReadFully(byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int n = _in.Read(buffer, offset, buffer.Length - offset);
                if (n == 0)
                {
                    return false;
                }

                offset += n;
            }
            return true;
        }

        // ── Dispatch (main thread) ────────────────────────────────────────────────

        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Wall-clock seconds since the Unix epoch, comparable across processes (matches Python's
        // time.time()); unlike Stopwatch, not usable for measuring local elapsed durations. Ticks-based
        // (100ns resolution) rather than ToUnixTimeMilliseconds(): on the same-machine stdio IPC leg,
        // real transfer time is sub-millisecond, so millisecond rounding could make
        // request_network_time read as 0.0 or negative.
        static double NowUnixSeconds()
        {
            return (DateTime.UtcNow - UnixEpoch).Ticks / (double)TimeSpan.TicksPerSecond;
        }

        void Dispatch(byte[] rawJson, double receivedWall, int frameBytes, long enqueuedTicks)
        {
            // Wait between the request being fully received (ReadLoop, background thread) and being
            // picked up here (main thread via EditorApplication.update). Stopwatch is monotonic across
            // the whole process, so this cross-thread difference is valid. NOT part of local_processing_time
            // below (that Stopwatch starts only now): tracked separately as dispatch_queue_time.
            double dispatchQueueTime = (Stopwatch.GetTimestamp() - enqueuedTicks) / (double)Stopwatch.Frequency;

            // Every frame on the wire counts here, including ones dropped below for parse/notify
            // reasons, matching the gateway-side external_timing.record_bytes(received=...) scope.
            _timing.RecordBytes(received: frameBytes);
            var sw = Stopwatch.StartNew();

            // Observation-only hook (mirrors ITransportServer.OnData); production code dispatches via MethodCaller below.
            OnData?.Invoke(ClientId, new ArraySegment<byte>(rawJson));

            RpcRequest req;
            double decodeTime;
            try
            {
                var decodeSw = Stopwatch.StartNew();
                req = JsonSerializer.Deserialize<RpcRequest>(rawJson);
                decodeTime = decodeSw.Elapsed.TotalSeconds;
            }
            catch (JsonException ex)
            {
                _getLogger().Info("StdioTransport JSON error: " + ex.Message);
                return;
            }

            if (string.IsNullOrEmpty(req.method))
            {
                _getLogger().Info("StdioTransport message missing/invalid 'method'");
                return;
            }

            JsonElement[] args = req.@params ?? Array.Empty<JsonElement>();

            // Notification (no id): fire and forget. Not timed (rpc_count/local_processing_time only
            // cover completed request/response cycles), only the byte count recorded above applies.
            if (req.id == null)
            {
                try { _caller.Call(req.method, args); }
                catch (Exception ex) { _getLogger().Info("Notify dispatch error " + req.method + ": " + Utils.UnwrapTargetInvocation(ex).Message); }
                return;
            }

            long id = req.id.Value;
            // Only echo "sent_at"/"received_at" back when the request carried a "sent_at": _timing is
            // always enabled on this transport, so gating on that (rather than on whether the sender
            // opted in) would add new response fields even for senders with RPC timing turned off.
            double? sentAt = req.sent_at;
            double requestNetworkTime = sentAt.HasValue ? receivedWall - sentAt.Value : 0.0;
            double? echoReceivedAt = sentAt.HasValue ? (double?)receivedWall : null;
            var stages = new RpcStageTimings();

            // Send() is called inline within each branch, BEFORE OnError is raised below: a
            // subscriber's OnError handler may rethrow (e.g. the integration test harness uses
            // OnError to surface errors loudly via ExceptionDispatchInfo.Throw()), which would
            // otherwise skip a deferred send-after-try/catch and leave the caller's request
            // hanging forever with no response.
            try
            {
                object result = _caller.Call(req.method, args, stages);
                // Taken here (after invoke, right before response framing) rather than before invoke,
                // so Unity API execution time and result serialization time do NOT leak into the
                // sender's measured response_network_time.
                double? echoSentAt = sentAt.HasValue ? (double?)NowUnixSeconds() : null;
                var encodeSw = Stopwatch.StartNew();
                byte[] frame = JsonRpcFraming.BuildResponse(id, result, null, _jsonOptions, echoSentAt, echoReceivedAt);
                double encodeTime = encodeSw.Elapsed.TotalSeconds;
                var sendSw = Stopwatch.StartNew();
                Send(frame);
                double sendTime = sendSw.Elapsed.TotalSeconds;
                _timing.RecordBytes(sent: frame.Length);
                _timing.RecordStages(stages, dispatchQueueTime, decodeTime, encodeTime, sendTime);
            }
            catch (Exception ex)
            {
                Exception reported = Utils.UnwrapTargetInvocation(ex);
                double? echoSentAt = sentAt.HasValue ? (double?)NowUnixSeconds() : null;
                var encodeSw = Stopwatch.StartNew();
                byte[] frame = JsonRpcFraming.BuildResponse(id, null, reported.Message, _jsonOptions, echoSentAt, echoReceivedAt);
                double encodeTime = encodeSw.Elapsed.TotalSeconds;
                var sendSw = Stopwatch.StartNew();
                Send(frame);
                double sendTime = sendSw.Elapsed.TotalSeconds;
                _timing.RecordBytes(sent: frame.Length);
                _timing.RecordStages(stages, dispatchQueueTime, decodeTime, encodeTime, sendTime);
                _getLogger().Info("RPC error " + req.method + ": " + reported.Message);
                OnError?.Invoke(ClientId, reported);
            }
            _timing.Record(localProcessingTime: sw.Elapsed.TotalSeconds, requestNetworkTime: requestNetworkTime);
        }
    }
}
