using System;
using System.Text.Json;

namespace LLiquidLink
{

    public class ObjectPrimitiveConverter : RpcJsonConverter<object, object>
    {

        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert == typeof(object);
        }

        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType == JsonTokenType.Null ? null : JsonPrimitiveHelper.ReadRaw(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            JsonPrimitiveHelper.WriteRaw(writer, value);
        }
    }
}
