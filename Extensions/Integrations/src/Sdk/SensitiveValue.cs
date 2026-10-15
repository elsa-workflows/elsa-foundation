using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Integrations.Sdk;

/// <summary>
/// A resolved secret payload with redaction by construction. <see cref="ToString"/> is always
/// <c>[redacted]</c>; there is no implicit conversion to <see cref="string"/>; JSON serialization
/// throws. Heuristic redactors remain a second net.
/// </summary>
[JsonConverter(typeof(SensitiveValueJsonConverter))]
public readonly struct SensitiveValue : IEquatable<SensitiveValue>
{
    public const string Redacted = "[redacted]";

    public SensitiveValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }

    public bool Equals(SensitiveValue other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SensitiveValue other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Redacted;

    public static bool operator ==(SensitiveValue left, SensitiveValue right) => left.Equals(right);

    public static bool operator !=(SensitiveValue left, SensitiveValue right) => !left.Equals(right);
}

internal sealed class SensitiveValueJsonConverter : JsonConverter<SensitiveValue>
{
    public override SensitiveValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("SensitiveValue cannot be deserialized from JSON.");

    public override void Write(Utf8JsonWriter writer, SensitiveValue value, JsonSerializerOptions options) =>
        throw new NotSupportedException("SensitiveValue cannot be serialized to JSON.");
}
