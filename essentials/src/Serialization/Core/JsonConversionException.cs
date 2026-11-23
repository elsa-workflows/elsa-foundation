using System.Text.Json;

namespace Elsa.Serialization.Core;

/// <summary>A safe, machine-readable refusal raised by the shared conversion contract.</summary>
public sealed class JsonConversionException(string code, string message, string? path = null) : JsonException($"{code}: {message}", path, null, null)
{
    /// <summary>The stable refusal code; the owning boundary supplies its protected path and guidance.</summary>
    public string Code { get; } = code;

    // MinimalEndpoints retains a body's JsonException.Message but discards its Path property.
    // API converters opt in so their owned member path survives that boundary. Runtime conversion
    // owners keep choosing the protected path themselves; carried JSON interiors are never scanned.
    internal bool AppendPathToMessage { get; init; }
    /// <inheritdoc />
    public override string Message => AppendPathToMessage && Path is { Length: > 0 } jsonPath
        ? $"{base.Message} Path: {jsonPath}." : base.Message;
}
