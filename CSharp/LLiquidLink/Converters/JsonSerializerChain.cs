using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
        /// <summary>Identifies a stage in the pre/main/fallback (de)serialization chain.</summary>
        public enum Stage
        {
            Pre = 0,
            Main = 1,
            Fallback = 2,
        }

        /// <summary>Type resolver for the pre stage: resolves only types with an explicitly registered
        /// converter, so any other type fails immediately and falls through to the main stage.</summary>
        class ConverterOnlyResolver : IJsonTypeInfoResolver
        {
            readonly IJsonTypeInfoResolver _inner = new DefaultJsonTypeInfoResolver();

            public JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
            {
                bool hasRegisteredConverter = options.Converters.Any(c => c.CanConvert(type));
                return !hasRegisteredConverter ? throw new NotSupportedException(type.FullName) : _inner.GetTypeInfo(type, options);
            }
        }

        struct StageConfig
        {
            public JsonSerializerOptions Options;
            public bool NullIsFailure;
            public bool ReadExceptionIsFatal;
        }

        readonly StageConfig[] _stageConfigs;

        /// <summary>The pre/main/fallback stage options, indexed by <see cref="Stage"/>. Fully initialized;
        /// callers add stage-specific converters after construction (e.g. <c>Options[(int)Stage.Main].Converters.Add(...)</c>).</summary>
        public JsonSerializerOptions[] Options { get; }

        /// <summary>Build a chain with the pre/main/fallback stage options fully initialized.</summary>
        public JsonSerializerChain()
        {
            var pre = new JsonSerializerOptions
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                TypeInfoResolver = new ConverterOnlyResolver(),
            };
            var main = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            var fallback = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

            Options = new JsonSerializerOptions[] { pre, main, fallback };

            // Pre: a null result counts as failure (falls through to main); a type mismatch (wrong
            //   converter for this token) is swallowed and falls through, but RpcJsonConverterReadException
            //   is fatal (propagates) so registry/orgType mismatches on registered objects surface as real
            //   errors instead of silently falling through to a confusing JsonElementLeakException.
            // Main: a null result is a valid success; RpcJsonConverterReadException is fatal (propagates,
            //   does not fall through to fallback) so registry/orgType mismatches surface as real errors.
            // Fallback: last stage, runs unconditionally as the final attempt.
            _stageConfigs = new StageConfig[]
            {
                new StageConfig { Options = pre,      NullIsFailure = true,  ReadExceptionIsFatal = true },
                new StageConfig { Options = main,     NullIsFailure = false, ReadExceptionIsFatal = true },
                new StageConfig { Options = fallback, NullIsFailure = false, ReadExceptionIsFatal = false },
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
        public JsonElement SerializeToElement(object value, Type type)
        {
            return Run(opts => JsonSerializer.SerializeToElement(value, type, opts));
        }

        T Run<T>(Func<JsonSerializerOptions, T> op)
        {
            for (int i = 0; i < _stageConfigs.Length; i++)
            {
                StageConfig stage = _stageConfigs[i];
                bool isLast = i == _stageConfigs.Length - 1;
                try
                {
                    T ret = op(stage.Options);
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
            return default(T);
        }
    }
}
