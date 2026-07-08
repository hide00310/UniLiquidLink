using System;
using System.Text.Json;

namespace LLiquidLink
{

    public class EnumConverter : RpcJsonConverter<System.Enum, RpcEnum>
    {

        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsEnum;
        }

        public override System.Enum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }
            var rpcObj = JsonSerializer.Deserialize<RpcEnum>(ref reader, DtoOptions);
            return rpcObj == null ? null : (System.Enum)Enum.Parse(typeToConvert, rpcObj.value);
        }

        public override void Write(Utf8JsonWriter writer, System.Enum value, JsonSerializerOptions options)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            JsonSerializer.Serialize(writer, new RpcEnum { value = value.ToString() }, DtoOptions);
        }
    }
}
