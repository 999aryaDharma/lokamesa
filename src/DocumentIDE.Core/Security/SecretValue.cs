using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocumentIDE.Core.Security;

[JsonConverter(typeof(SecretValueJsonConverter))]
public class SecretValue
{
    public string Value { get; }

    public SecretValue(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override string ToString()
    {
        return "[REDACTED]";
    }
}

public class SecretValueJsonConverter : JsonConverter<SecretValue>
{
    public override SecretValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var val = reader.GetString() ?? string.Empty;
        return new SecretValue(val);
    }

    public override void Write(Utf8JsonWriter writer, SecretValue value, JsonSerializerOptions options)
    {
        writer.WriteStringValue("[REDACTED]");
    }
}
