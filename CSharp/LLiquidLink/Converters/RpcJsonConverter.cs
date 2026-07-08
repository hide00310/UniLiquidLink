using System;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LLiquidLink
{
    /// <summary>
    /// Base class for JSON converters that translate between an original .NET type
    /// and an RPC-wire DTO type.
    /// </summary>
    /// <typeparam name="TOrg">The original .NET type (e.g. <c>UnityEngine.Object</c>).</typeparam>
    /// <typeparam name="TRpc">The RPC wire DTO type (e.g. <c>RpcUnityObject</c>).</typeparam>
    public abstract class RpcJsonConverter<TOrg, TRpc> : JsonConverter<TOrg>
        where TOrg : class
        where TRpc : class
    {
        /// <summary>Fully qualified name of the RPC DTO type used as a discriminator on the wire.</summary>
        public string rpcTypeName => typeof(TRpc).FullName;

        /// <summary>The original .NET type this converter handles.</summary>
        public Type orgType => typeof(TOrg);

        /// <summary>
        /// Options used to (de)serialize the wire DTO itself, independent of whichever
        /// JsonSerializerOptions instance (main/pre/fallback) hosts this converter. The DTO
        /// types only contain primitive fields, so no custom converters or resolvers are needed;
        /// reusing the runtime-supplied options here would break under a restricted resolver
        /// (e.g. the pre-chain's ConverterOnlyResolver) that requires every type it resolves
        /// to have its own registered converter.
        /// </summary>
        protected static readonly JsonSerializerOptions DtoOptions =
            new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    }

    /// <summary>Shared helpers for reading/writing JSON primitives directly (no wrapper DTO).</summary>
    internal static class JsonPrimitiveHelper
    {
        /// <summary>True for CLR primitive-ish types that should bypass the Unity-object registry envelope.</summary>
        public static bool IsPrimitive(object value)
        {
            return value is string or bool
                or int or long or short
                or double or float or decimal;
        }

        /// <summary>Read the current JSON token directly as a boxed CLR primitive.</summary>
        public static object ReadRaw(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String: return reader.GetString();
                case JsonTokenType.True: return true;
                case JsonTokenType.False: return false;
                case JsonTokenType.Number:
                    return reader.TryGetInt64(out long l) ? (object)l : reader.GetDouble();
                default:
                    throw new NotSupportedException("Unsupported primitive token: " + reader.TokenType);
            }
        }

        /// <summary>Write a boxed CLR primitive directly as its native JSON token.</summary>
        public static void WriteRaw(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case string s: writer.WriteStringValue(s); break;
                case bool b: writer.WriteBooleanValue(b); break;
                case int i: writer.WriteNumberValue(i); break;
                case long l: writer.WriteNumberValue(l); break;
                case short sh: writer.WriteNumberValue(sh); break;
                case double d: writer.WriteNumberValue(d); break;
                case float f: writer.WriteNumberValue(f); break;
                case decimal m: writer.WriteNumberValue(m); break;
                default: throw new NotSupportedException("Unsupported primitive value: " + value.GetType());
            }
        }
    }

    public class RpcJsonConverterReadException : Exception
    {
        public RpcJsonConverterReadException()
        {
        }

        public RpcJsonConverterReadException(string message) : base(message)
        {
        }

        public RpcJsonConverterReadException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected RpcJsonConverterReadException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
