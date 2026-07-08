using LLiquidLink.Logger;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LLiquidLink
{
    /// <summary>Handles the 4-byte big-endian length prefix + JSON-RPC 2.0 protocol over WebSocket (test injection path).</summary>
    class JsonRpcProtocol
    {
        readonly RpcBus _bus;
        readonly Action<byte[]> _send;
        readonly Func<ILogger> _getLogger;
        readonly JsonSerializerOptions _jsonOptions;
        readonly Action<Exception> _onError;

        /// <summary>Initialize the protocol handler.</summary>
        /// <param name="bus">RPC dispatcher used to invoke registered methods.</param>
        /// <param name="send">Callback that transmits a raw byte response to the client.</param>
        /// <param name="getLogger">Factory that returns the current logger.</param>
        /// <param name="jsonOptions">JSON serializer options shared with the bus.</param>
        /// <param name="onError">Optional callback invoked on parse or dispatch errors.</param>
        public JsonRpcProtocol(RpcBus bus, Action<byte[]> send, Func<ILogger> getLogger, JsonSerializerOptions jsonOptions, Action<Exception> onError)
        {
            _bus = bus;
            _send = send;
            _getLogger = getLogger;
            _jsonOptions = jsonOptions;
            _onError = onError;
        }

        /// <summary>Parse and dispatch a raw WebSocket message, then send the JSON-RPC response.</summary>
        /// <param name="raw">Raw bytes received from the client (4-byte length header + UTF-8 JSON body).</param>
        public void HandleMessage(byte[] raw)
        {
            _getLogger().DebugFormat("WS Recv: {0}", Encoding.UTF8.GetString(raw, 4, raw.Length - 4));

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(raw.AsMemory(4));
            }
            catch (JsonException ex)
            {
                _getLogger().DebugFormat("[JSON Error] {0}", ex.Message);
                _onError?.Invoke(ex);
                return;
            }

            using (doc)
            {
                var root = doc.RootElement;
                string method = root.GetProperty("method").GetString();
                string idJson = root.GetProperty("id").GetRawText();

                JsonElement[] args = Array.Empty<JsonElement>();
                if (root.TryGetProperty("params", out JsonElement paramsEl))
                {
                    args = paramsEl.EnumerateArray().ToArray();
                }

                try
                {
                    object result = _bus.Dispatch(method, args);
                    SendBytes(JsonRpcFraming.BuildResponse(idJson, result, null, _jsonOptions));
                }
                catch (Exception ex)
                {
                    // RPC dispatch errors are returned to the client as JSON-RPC error responses.
                    SendBytes(JsonRpcFraming.BuildResponse(idJson, null, ex.Message, _jsonOptions));
                    _getLogger().DebugFormat("[RPC Error] {0}: {1}", method, ex.Message);
                    _onError?.Invoke(ex);
                }
            }
        }

        /// <summary>Log and transmit a raw byte response to the client.</summary>
        /// <param name="bytes">Framed response bytes.</param>
        void SendBytes(byte[] bytes)
        {
            _getLogger().DebugFormat("WS Send: {0}", Encoding.UTF8.GetString(bytes, 4, bytes.Length - 4));
            _send(bytes);
        }
    }
}
