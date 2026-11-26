using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Connections.Core.Models;

/// <summary>
/// A secret or token that has passed through activity or connection code. <see cref="ToString"/>
/// returns <c>[redacted]</c>, there is no implicit conversion to <see cref="string"/>, and JSON
/// serialization throws, so the value cannot leak into workflow state, logs or evidence by
/// construction.
/// </summary>
[JsonConverter(typeof(SensitiveValueJsonConverter))]
public readonly struct SensitiveValue : IEquatable<SensitiveValue>
{
    public const string Redacted = "[redacted]";

    private readonly string _value;

    public SensitiveValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>The raw value. Callers must not persist, log or return it.</summary>
    public string Reveal() => _value;

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public override string ToString() => Redacted;

    public bool Equals(SensitiveValue other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SensitiveValue other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);

    public static bool operator ==(SensitiveValue left, SensitiveValue right) => left.Equals(right);

    public static bool operator !=(SensitiveValue left, SensitiveValue right) => !left.Equals(right);
}

/// <summary>Refuses to serialize or deserialize a <see cref="SensitiveValue"/>.</summary>
public sealed class SensitiveValueJsonConverter : JsonConverter<SensitiveValue>
{
    public override SensitiveValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("SensitiveValue cannot be serialized or deserialized.");

    public override void Write(Utf8JsonWriter writer, SensitiveValue value, JsonSerializerOptions options) =>
        throw new NotSupportedException("SensitiveValue cannot be serialized or deserialized.");
}
