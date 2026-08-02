using System;
using System.Text.Json;
using LLiquidLink;

namespace UniLiquidLink
{

    public class InstanceObjectConverter<T> : RpcJsonConverter<T, RpcInstanceObject> where T : class
    {
        internal readonly ObjectRegistry _registry;

        public InstanceObjectConverter(ObjectRegistry registry)
        {
            _registry = registry;
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
            if (value == null) { writer.WriteNullValue(); return; }
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
