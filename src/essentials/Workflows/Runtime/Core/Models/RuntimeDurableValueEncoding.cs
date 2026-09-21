using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The storage-specific durable representation of one runtime value.</summary>
public sealed record RuntimeDurableValueEncoding(
    DurableValueStorage Storage,
    JsonElement? InlineValue,
    DurableValueExternalReference? ExternalReference);

/// <summary>Presence-preserving result for a nullable durable-value read.</summary>
public sealed record RuntimeDurableValueReadResult(bool Exists, object? Value)
{
    public static RuntimeDurableValueReadResult Missing { get; } = new(false, null);
}

public static class WellKnownRuntimeDurableValueStorageDrivers
{
    public const string Json = "elsa.json";
}
