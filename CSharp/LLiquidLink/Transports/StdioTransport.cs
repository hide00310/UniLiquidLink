using LLiquidLink.Logger;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace LLiquidLink
{
    /// <summary>Reads JSON-RPC requests from a stream with 4-byte big-endian length framing, dispatches via RpcBus, and writes responses.</summary>
    public class StdioTransport
    {
        readonly IMainThreadDispatcher _dispatcher;
        readonly RpcBus _bus;
        readonly JsonSerializerOptions _jsonOptions;
        readonly Func<ILogger> _getLogger;
        readonly Action<Exception> _onError;
        readonly object _sendLock = new object();

        Stream _in;
        Stream _out;
        Thread _readThread;
        bool _running;

        const int ClientId = 1;

        /// <summary>Fired on the main thread when the stdio connection is established.</summary>
        public event Action<int> OnConnect;

        /// <summary>Fired on the main thread when the stdin stream closes.</summary>
        public event Action<int> OnDisconnect;

        /// <summary>Initialize StdioTransport with its dependencies.</summary>
        public StdioTransport(
            IMainThreadDispatcher dispatcher,
            RpcBus bus,
            JsonSerializerOptions jsonOptions,
            Func<ILogger> getLogger,
            Action<Exception> onError = null)
        {
            _dispatcher = dispatcher;
            _bus = bus;
            _jsonOptions = jsonOptions;
            _getLogger = getLogger;
            _onError = onError;
        }

        /// <summary>Start the read loop on a background thread and fire OnConnect.</summary>
        /// <param name="inStream">Stream to read requests from (Python middleware's stdout).</param>
        /// <param name="outStream">Stream to write responses to (Python middleware's stdin).</param>
        public void Start(Stream inStream, Stream outStream)
        {
            _in = inStream;
            _out = outStream;
            _running = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "StdioTransport" };
            _readThread.Start();
            _dispatcher.Enqueue(() => OnConnect?.Invoke(ClientId));
        }

        /// <summary>Signal the read loop to stop.</summary>
        public void Stop()
        {
            _running = false;
        }

        // ── Framing ──────────────────────────────────────────────────────────────

        void Send(byte[] frame)
        {
            lock (_sendLock)
            {
                _out.Write(frame, 0, frame.Length);
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
                _getLogger().Info("StdioTransport read error: " + ex.Message);
                _onError?.Invoke(ex);
            }

            if (_running)
            {
                _running = false;
                _dispatcher.Enqueue(() => OnDisconnect?.Invoke(ClientId));
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
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(rawJson);
            }
            catch (JsonException ex)
            {
                _getLogger().Info("StdioTransport JSON error: " + ex.Message);
                return;
            }

            using (doc)
            {
                var root = doc.RootElement;
                string method = root.GetProperty("method").GetString();

                JsonElement[] args = root.TryGetProperty("params", out JsonElement p)
                    ? p.EnumerateArray().ToArray()
                    : Array.Empty<JsonElement>();

                // Notification (no id): fire and forget
                if (!root.TryGetProperty("id", out JsonElement idEl))
                {
                    try { _bus.Dispatch(method, args); }
                    catch (Exception ex) { _getLogger().Info("Notify dispatch error " + method + ": " + ex.Message); }
                    return;
                }

                string idJson = idEl.GetRawText();
                try
                {
                    object result = _bus.Dispatch(method, args);
                    Send(JsonRpcFraming.BuildResponse(idJson, result, null, _jsonOptions));
                }
                catch (Exception ex)
                {
                    Send(JsonRpcFraming.BuildResponse(idJson, null, ex.Message, _jsonOptions));
                    _getLogger().Info("RPC error " + method + ": " + ex.Message);
                    _onError?.Invoke(ex);
                }
            }
        }
    }
}
