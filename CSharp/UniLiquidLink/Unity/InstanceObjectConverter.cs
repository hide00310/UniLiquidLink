using System;
using System.Text.Json;
using LLiquidLink;

namespace UniLiquidLink
{

    // NOTE: hand-written. Generation from tools~/converters/converters.csv is disabled (hand_written=true),
    // so edit this file directly. Null handling is customised by injecting an IRpcNullChecker<T>
    // through the constructor; no member needs to be overridden.

    /// <summary>
    /// Converter that maps live object instances to <see cref="RpcInstanceObject"/> descriptors via the shared registry.
    /// An injected <see cref="IRpcNullChecker{T}"/> can widen what is written as JSON null.
    /// </summary>
    /// <typeparam name="T">The original object type the converter handles.</typeparam>
    public class InstanceObjectConverter<T> : RpcJsonConverter<T, RpcInstanceObject> where T : class
    {
        internal readonly ObjectRegistry _registry;

        /// <summary>Optional injected null definition; <c>null</c> means only reference-null is written as JSON null.</summary>
        readonly IRpcNullChecker<T> _nullChecker;

        /// <summary>Initialize the converter with its registry and an optional null checker.</summary>
        /// <param name="registry">Registry mapping instance IDs to live objects.</param>
        /// <param name="nullChecker">Defines which non-reference-null values are also written as JSON null; <c>null</c> for none.</param>
        public InstanceObjectConverter(ObjectRegistry registry, IRpcNullChecker<T> nullChecker = null)
        {
            _registry = registry;
            _nullChecker = nullChecker;
        }

        public override bool CanConvert(Type typeToConvert)
        {
            return typeof(T).IsAssignableFrom(typeToConvert);
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }
            var rpcObj = JsonSerializer.Deserialize<RpcInstanceObject>(ref reader, DtoOptions);
            if (rpcObj == null)
            {
                return null;
            }

            var ret = _registry.GetObject(rpcObj.instanceId);
            if (ret == null) throw new RpcJsonConverterReadException($"Object {rpcObj.instanceId} not found");

            if (rpcObj.orgType != ret.GetType().FullName) throw new RpcJsonConverterReadException($"Object {rpcObj.orgType} != {ret.GetType().FullName}");
            if (!typeToConvert.IsAssignableFrom(ret.GetType())) throw new RpcJsonConverterReadException($"Object {rpcObj.orgType} != {typeToConvert.FullName}");
            return (T)ret;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            // The injected checker also covers values it reports as null (e.g. Unity fake-null objects),
            // which must neither be registered nor sent as an object descriptor.
            if (value == null || (_nullChecker != null && _nullChecker.IsNull(value))) { writer.WriteNullValue(); return; }
            long id = _registry.RegisterObject(value);
            JsonSerializer.Serialize(writer, new RpcInstanceObject
            {
                rpcType = typeof(RpcInstanceObject).FullName,
                instanceId = id,
                orgType = value.GetType().FullName,
                name = value.ToString()
            }, DtoOptions);
        }
    }
}
