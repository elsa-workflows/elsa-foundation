using System.Text.Json;

namespace Elsa.Serialization.Core;

/// <summary>A safe, machine-readable refusal raised by the shared conversion contract.</summary>
public sealed class JsonConversionException(string code, string message, string? path = null) : JsonException($"{code}: {message}", path, null, null)
{
    /// <summary>The stable refusal code; the owning boundary supplies its protected path and guidance.</summary>
    public string Code { get; } = code;
}
