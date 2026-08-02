using LLiquidLink.Logger;
using System;
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
                    _dispatcher.Enqueue(() => Dispatch(captured));
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

        void Dispatch(byte[] rawJson)
        {
            // Observation-only hook (mirrors ITransportServer.OnData); production code dispatches via MethodCaller below.
            OnData?.Invoke(ClientId, new ArraySegment<byte>(rawJson));

            RpcRequest req;
            try
            {
                req = JsonSerializer.Deserialize<RpcRequest>(rawJson);
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

            // Notification (no id): fire and forget
            if (req.id == null)
            {
                try { _caller.Call(req.method, args); }
                catch (Exception ex) { _getLogger().Info("Notify dispatch error " + req.method + ": " + Utils.UnwrapTargetInvocation(ex).Message); }
                return;
            }

            long id = req.id.Value;
            try
            {
                object result = _caller.Call(req.method, args);
                Send(JsonRpcFraming.BuildResponse(id, result, null, _jsonOptions));
            }
            catch (Exception ex)
            {
                Exception reported = Utils.UnwrapTargetInvocation(ex);
                Send(JsonRpcFraming.BuildResponse(id, null, reported.Message, _jsonOptions));
                _getLogger().Info("RPC error " + req.method + ": " + reported.Message);
                OnError?.Invoke(ClientId, reported);
            }
        }
    }
}
