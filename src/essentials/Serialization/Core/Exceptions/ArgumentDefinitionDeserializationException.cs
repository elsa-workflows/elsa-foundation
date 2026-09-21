namespace Elsa.Serialization.Core.Exceptions;

/// <summary>
/// Thrown when an authored argument definition (variable/input/output) cannot be deserialized from its
/// persisted JSON. Wraps the underlying <see cref="System.Text.Json.JsonException"/> so that a raw
/// infrastructure exception does not escape the (de)serialization boundary (§2.23.5); the original cause is
/// preserved on <see cref="System.Exception.InnerException"/>.
/// </summary>
public sealed class ArgumentDefinitionDeserializationException : Exception
{
    public ArgumentDefinitionDeserializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
