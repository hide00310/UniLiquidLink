using LLiquidLink;
using System;
using System.Text.Json;

namespace UniLiquidLink
{

    public class InstanceObjectConverter<T> : RpcJsonConverter<T, RpcUnityObject> where T : UnityEngine.Object
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
            var rpcObj = JsonSerializer.Deserialize<RpcUnityObject>(ref reader, DtoOptions);
            if (rpcObj == null)
            {
                return null;
            }

            var ret = (T)_registry.GetObject(rpcObj.instanceId);
            if (ret == null)
            {
                throw new ArgumentException($"Object {rpcObj.instanceId} not found");
            }

            if (rpcObj.orgType != ret.GetType().FullName)
            {
                throw new RpcJsonConverterReadException($"Object {rpcObj.orgType} != {ret.GetType().FullName}");
            }

            return !typeToConvert.IsAssignableFrom(ret.GetType())
                ? throw new RpcJsonConverterReadException($"Object {rpcObj.orgType} != {typeToConvert.FullName}")
                : ret;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            long id = _registry.RegisterObject(value);
            JsonSerializer.Serialize(writer, new RpcUnityObject
            {
                rpcType = typeof(RpcUnityObject).FullName,
                instanceId = id,
                orgType = value.GetType().FullName,
                name = value.name
            }, DtoOptions);
        }
    }
}
