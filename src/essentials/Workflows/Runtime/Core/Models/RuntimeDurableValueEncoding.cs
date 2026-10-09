using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The storage-specific durable representation of one runtime value.</summary>
public sealed record RuntimeDurableValueEncoding(
    DurableValueStorage Storage,
    JsonElement? InlineValue,
    DurableValueExternalReference? ExternalReference)
{
    /// <summary>
    /// The withheld envelope (spec 188, FR-010) a workflow-variable output capture writes into the variable frame
    /// instead of a value, or null for an encoding a storage driver produced. Set only by <see cref="ForWithheld"/>; such
    /// an encoding carries no inline value and no external reference, and no storage driver sees the value.
    /// </summary>
    public ValueEnvelope? Withheld { get; private init; }

    /// <summary>Wraps a withheld envelope for a workflow-variable output capture; see <see cref="Withheld"/>.</summary>
    public static RuntimeDurableValueEncoding ForWithheld(ValueEnvelope withheld)
    {
        ArgumentNullException.ThrowIfNull(withheld);
        if (withheld.Presence != ValuePresence.Withheld)
            throw new ArgumentException("Only a withheld envelope can be carried as a withheld encoding.", nameof(withheld));
        return new(withheld.Policy.Storage, null, null) { Withheld = withheld };
    }
}

/// <summary>Presence-preserving result for a nullable durable-value read.</summary>
public sealed record RuntimeDurableValueReadResult(bool Exists, object? Value)
{
    public static RuntimeDurableValueReadResult Missing { get; } = new(false, null);
}

public static class WellKnownRuntimeDurableValueStorageDrivers
{
    public const string Json = "elsa.json";
}
