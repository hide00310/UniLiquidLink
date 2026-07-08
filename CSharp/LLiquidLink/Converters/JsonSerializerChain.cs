using System;
using System.Runtime.Serialization;
using System.Text.Json;

namespace LLiquidLink
{
    /// <summary>
    /// Thrown when deserialization yields a raw <see cref="JsonElement"/> instead of a converted value,
    /// meaning no stage actually produced a real CLR value for the target type.
    /// </summary>
    public class JsonElementLeakException : Exception
    {
        public JsonElementLeakException()
        {
        }

        public JsonElementLeakException(string message) : base(message)
        {
        }

        public JsonElementLeakException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected JsonElementLeakException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// Holds the pre/main/fallback <see cref="JsonSerializerOptions"/> stages and runs
    /// (de)serialization through them in order, returning the first stage's success.
    /// </summary>
    public class JsonSerializerChain
    {
        struct Stage
        {
            public JsonSerializerOptions Options;
            public bool NullIsFailure;
            public bool ReadExceptionIsFatal;
        }

        readonly Stage[] _stages;

        /// <summary>Options for the pre stage (tried first; only succeeds for types with an explicit converter).</summary>
        public JsonSerializerOptions Pre { get; }

        /// <summary>Options for the main stage (the general-purpose serializer).</summary>
        public JsonSerializerOptions Main { get; }

        /// <summary>Options for the fallback stage (tried when main fails, e.g. Unity value types via JsonUtility).</summary>
        public JsonSerializerOptions Fallback { get; }

        /// <summary>Build a chain from the three stage options.</summary>
        /// <param name="pre">Pre-stage options.</param>
        /// <param name="main">Main-stage options.</param>
        /// <param name="fallback">Fallback-stage options.</param>
        public JsonSerializerChain(JsonSerializerOptions pre, JsonSerializerOptions main, JsonSerializerOptions fallback)
        {
            Pre = pre;
            Main = main;
            Fallback = fallback;

            // Pre: a null result counts as failure (falls through to main); any exception is swallowed.
            // Main: a null result is a valid success; RpcJsonConverterReadException is fatal (propagates,
            //   does not fall through to fallback) so registry/orgType mismatches surface as real errors.
            // Fallback: last stage, runs unconditionally as the final attempt.
            _stages = new Stage[]
            {
                new Stage { Options = pre,      NullIsFailure = true,  ReadExceptionIsFatal = false },
                new Stage { Options = main,     NullIsFailure = false, ReadExceptionIsFatal = true },
                new Stage { Options = fallback, NullIsFailure = false, ReadExceptionIsFatal = false },
            };
        }

        /// <summary>Deserialize <paramref name="rawJson"/> to <paramref name="type"/>, trying each stage in order.</summary>
        /// <param name="rawJson">Raw JSON text to deserialize.</param>
        /// <param name="type">Target .NET type.</param>
        /// <returns>The deserialized value from the first successful stage.</returns>
        /// <exception cref="JsonElementLeakException">
        /// Thrown when <paramref name="type"/> is not <see cref="JsonElement"/> itself but no stage converted
        /// the value, leaving a raw <see cref="JsonElement"/> as the result.
        /// </exception>
        public object Deserialize(string rawJson, Type type)
        {
            object ret = Run(opts => JsonSerializer.Deserialize(rawJson, type, opts));
            return type != typeof(JsonElement) && ret is JsonElement
                ? throw new JsonElementLeakException(string.Format(
                    "Deserialize to {0} yielded a raw JsonElement instead of a converted value " +
                    "(no stage produced a real CLR value). raw={1}", type.FullName, rawJson))
                : ret;
        }

        /// <summary>Serialize <paramref name="value"/> to a <see cref="JsonElement"/>, trying each stage in order.</summary>
        /// <param name="value">Object to serialize.</param>
        /// <param name="type">Runtime type of <paramref name="value"/>.</param>
        /// <returns>The serialized element from the first successful stage.</returns>
        public object SerializeToElement(object value, Type type)
        {
            return Run(opts => JsonSerializer.SerializeToElement(value, type, opts));
        }

        object Run(Func<JsonSerializerOptions, object> op)
        {
            for (int i = 0; i < _stages.Length; i++)
            {
                Stage stage = _stages[i];
                bool isLast = i == _stages.Length - 1;
                try
                {
                    object ret = op(stage.Options);
                    if (ret != null || !stage.NullIsFailure || isLast)
                    {
                        return ret;
                    }
                }
                catch (RpcJsonConverterReadException) when (stage.ReadExceptionIsFatal && !isLast)
                {
                    throw;
                }
                catch (Exception) when (!isLast)
                {
                }
            }
            return null;
        }
    }
}
