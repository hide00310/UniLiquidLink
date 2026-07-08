using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace UniLiquidLink
{
    /// <summary>Routes matching value types to JsonUtility so System.Text.Json can compose them inside collections.</summary>
    public class JsonUtilityConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsValueType && !typeToConvert.IsPrimitive && !typeToConvert.IsEnum && Nullable.GetUnderlyingType(typeToConvert) == null;
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return (JsonConverter)Activator.CreateInstance(typeof(JsonUtilityConverter<>).MakeGenericType(typeToConvert));
        }
    }

    /// <summary>Delegates single-value (de)serialization to Unity's JsonUtility, bridging System.Text.Json tokens via a raw JSON string.</summary>
    internal class JsonUtilityConverter<T> : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            string rawJson = doc.RootElement.GetRawText();
            object result;
            result = JsonUtility.FromJson(rawJson, typeToConvert);
            return (T)result;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            string json = JsonUtility.ToJson(value);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.WriteTo(writer);
        }
    }
}
