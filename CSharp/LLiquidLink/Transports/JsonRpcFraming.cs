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
        public static byte[] BuildResponse(long id, object result, string error, JsonSerializerOptions opts)
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

            string body = error == null
                ? "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString() + ",\"result\":" + resultJson + "}"
                : "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString() + ",\"error\":{\"code\":-32603,\"message\":" + JsonSerializer.Serialize(error) + "}}";
            return WrapFrame(Encoding.UTF8.GetBytes(body));
        }
    }
}
