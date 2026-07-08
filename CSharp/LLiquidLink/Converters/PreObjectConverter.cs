using System;
using System.Text.Json;

namespace LLiquidLink
{

    public class PreObjectConverter : RpcJsonConverter<object, RpcUnityObject>
    {
        readonly ObjectRegistry _registry;

        public PreObjectConverter(ObjectRegistry registry)
        {
            _registry = registry;
        }

        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert == typeof(object);
        }

        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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

            var ret = _registry.GetObject(rpcObj.instanceId);
            if (ret == null)
            {
                throw new ArgumentException($"Object {rpcObj.instanceId} not found");
            }

            return rpcObj.orgType != ret.GetType().FullName
                ? throw new RpcJsonConverterReadException($"Object {rpcObj.orgType} != {ret.GetType().FullName}")
                : ret;
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            throw new NotSupportedException();
        }
    }
}
