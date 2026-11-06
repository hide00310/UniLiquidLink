using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LLiquidLink
{
    // Shared helpers: 4-byte little-endian length prefix and JSON-RPC 2.0 response builder.
    public static class JsonRpcFraming
    {
        // Prepend a 4-byte little-endian length to body.
        public static byte[] WrapFrame(byte[] body)
        {
            var frame = new byte[4 + body.Length];
            BitConverter.GetBytes(body.Length).CopyTo(frame, 0);
            body.CopyTo(frame, 4);
            return frame;
        }

        // Read a 4-byte little-endian length from stream. Returns -1 on EOF.
        public static int ReadFrameLength(Stream stream)
        {
            var buf = new byte[4];
            int offset = 0;
            while (offset < 4)
            {
                int n = stream.Read(buf, offset, 4 - offset);
                if (n == 0)
                {
                    return -1;
                }

                offset += n;
            }
            return BitConverter.ToInt32(buf, 0);
        }

        // Build a framed JSON-RPC 2.0 response (4-byte little-endian length prefix + UTF-8 body).
        // sentAt, when non-null, is echoed back as a "sent_at" field so the sender can measure wall-clock
        // response transit time; only set when the request itself carried a "sent_at" (see StdioTransport.Dispatch).
        // receivedAt, when non-null, is echoed back as a "received_at" field (the wall-clock instant this
        // side finished receiving the request frame) so the sender can derive request_network_time/remote_time;
        // follows the same "only when the request carried a sent_at" gating as sentAt.
        public static byte[] BuildResponse(long id, object result, string error, JsonSerializerOptions opts, double? sentAt = null, double? receivedAt = null)
        {
            string resultJson;
            if (result == null)
            {
                resultJson = "null";
            }
            else if (result is JsonElement je)
            {
                resultJson = je.GetRawText();
            }
            else
            {
                resultJson = JsonSerializer.Serialize(result, opts);
            }

            string sentAtField = sentAt.HasValue ? ",\"sent_at\":" + JsonSerializer.Serialize(sentAt.Value) : "";
            string receivedAtField = receivedAt.HasValue ? ",\"received_at\":" + JsonSerializer.Serialize(receivedAt.Value) : "";
            string body = error == null
                ? "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString() + ",\"result\":" + resultJson + sentAtField + receivedAtField + "}"
                : "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString() + ",\"error\":{\"code\":-32603,\"message\":" + JsonSerializer.Serialize(error) + "}" + sentAtField + receivedAtField + "}";
            return WrapFrame(Encoding.UTF8.GetBytes(body));
        }
    }
}
