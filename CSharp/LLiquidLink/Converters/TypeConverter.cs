using System;
using System.Text.Json;

namespace LLiquidLink
{

    public class TypeConverter : RpcJsonConverter<Type, RpcType>
    {
        readonly TypeResolver _resolver;

        public TypeConverter(TypeResolver resolver)
        {
            _resolver = resolver;
        }

        public override Type Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }
            var rpcObj = JsonSerializer.Deserialize<RpcType>(ref reader, DtoOptions);
            return rpcObj == null ? null : _resolver.Resolve(rpcObj.value);
        }

        public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            JsonSerializer.Serialize(writer, new RpcType { value = value.FullName }, DtoOptions);
        }
    }
}
