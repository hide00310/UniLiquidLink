using System.IO;
using System.Text;
using System.Text.Json;

namespace LLiquidLink
{
    // Shared helpers: 4-byte big-endian length prefix and JSON-RPC 2.0 response builder.
    internal static class JsonRpcFraming
    {
        // Prepend a 4-byte big-endian length to body.
        internal static byte[] WrapFrame(byte[] body)
        {
            var frame = new byte[4 + body.Length];
            int len = body.Length;
            frame[0] = (byte)(len >> 24);
            frame[1] = (byte)(len >> 16);
            frame[2] = (byte)(len >> 8);
            frame[3] = (byte)len;
            body.CopyTo(frame, 4);
            return frame;
        }

        // Read a 4-byte big-endian length from stream. Returns -1 on EOF.
        internal static int ReadFrameLength(Stream stream)
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
            return (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
        }

        // Build a framed JSON-RPC 2.0 response (4-byte big-endian length prefix + UTF-8 body).
        internal static byte[] BuildResponse(string idJson, object result, string error, JsonSerializerOptions opts)
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
                ? "{\"jsonrpc\":\"2.0\",\"id\":" + idJson + ",\"result\":" + resultJson + "}"
                : "{\"jsonrpc\":\"2.0\",\"id\":" + idJson + ",\"error\":{\"code\":-32603,\"message\":" + JsonSerializer.Serialize(error) + "}}";
            return WrapFrame(Encoding.UTF8.GetBytes(body));
        }
    }
}
