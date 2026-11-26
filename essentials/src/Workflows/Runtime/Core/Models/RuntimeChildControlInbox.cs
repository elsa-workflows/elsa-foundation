using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Versioned acceptance state on one exact calling invocation. Data alone grants no source or consumer authority.
/// The cursor and pending removals must commit atomically with the consumer decision and its owed work.
/// </summary>
[JsonConverter(typeof(RuntimeChildControlInboxJsonConverter))]
public sealed class RuntimeChildControlInbox
{
    public const int CurrentVersion = 1;

    public RuntimeChildControlInbox(int version, RuntimeChildControlTarget target, long nextOrdinal,
        bool isTerminalConsumed, IReadOnlyList<RuntimeChildControlTransport>? pending = null)
    {
        if (version != CurrentVersion)
            throw new ArgumentOutOfRangeException(nameof(version), "Unsupported child-control inbox version.");
        ArgumentNullException.ThrowIfNull(target);
        if (nextOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(nextOrdinal));
        var snapshot = (pending ?? []).OrderBy(item => item?.Batch.Ordinal).ToArray();
        if (snapshot.Any(item => item is null || !target.MatchesAddress(item) || item.Batch.Ordinal < nextOrdinal))
            throw new ArgumentException("Pending control must belong to this exact open stream at or after its cursor.", nameof(pending));
        if (snapshot.Select(item => item.Batch.Ordinal).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Pending control ordinals must be unique.", nameof(pending));
        var terminals = snapshot.Where(item => item.Batch.IsTerminal).ToArray();
        if (terminals.Length > 1 || terminals.Length == 1 && snapshot[^1] != terminals[0])
            throw new ArgumentException("A pending terminal barrier must end its stream.", nameof(pending));
        if (isTerminalConsumed && (snapshot.Length != 0 || nextOrdinal == 0))
            throw new ArgumentException("A consumed terminal stream cannot retain pending control or an initial cursor.", nameof(isTerminalConsumed));

        Version = version;
        Target = target;
        NextOrdinal = nextOrdinal;
        IsTerminalConsumed = isTerminalConsumed;
        Pending = Array.AsReadOnly(snapshot);
    }

    public int Version { get; }
    public RuntimeChildControlTarget Target { get; }
    /// <summary>Next expected ordinal while open; a sealed terminal at Int64.MaxValue leaves this saturated.</summary>
    public long NextOrdinal { get; }
    public bool IsTerminalConsumed { get; }
    public IReadOnlyList<RuntimeChildControlTransport> Pending { get; }

    /// <summary>Queries committed acceptance after independent exact-source authorization; this data query grants no authority.</summary>
    public bool HasAccepted(RuntimeChildControlTransport transport)
    {
        if (!Target.MatchesAddress(transport)) return false;
        if (transport.Batch.Ordinal < NextOrdinal || IsTerminalConsumed && NextOrdinal == long.MaxValue && transport.Batch.Ordinal == long.MaxValue)
            return true;
        return Pending.Any(item => item.Batch.Ordinal == transport.Batch.Ordinal &&
            JsonElement.DeepEquals(JsonSerializer.SerializeToElement(item), JsonSerializer.SerializeToElement(transport)));
    }

}

internal sealed class RuntimeChildControlInboxJsonConverter : JsonConverter<RuntimeChildControlInbox>
{
    public RuntimeChildControlInboxJsonConverter() { }

    public override RuntimeChildControlInbox Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = JsonSerializer.Deserialize<WireInbox>(ref reader, options)
            ?? throw new JsonException("A child-control inbox must be an object.");
        if (wire.NextOrdinal is null || wire.IsTerminalConsumed is null || wire.Pending is null)
            throw new JsonException("A child-control inbox must declare its cursor, terminal status and pending collection.");
        return new RuntimeChildControlInbox(wire.Version, wire.Target, wire.NextOrdinal.Value,
            wire.IsTerminalConsumed.Value, wire.Pending);
    }

    public override void Write(Utf8JsonWriter writer, RuntimeChildControlInbox value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new WireInbox(value.Version, value.Target, value.NextOrdinal,
            value.IsTerminalConsumed, value.Pending), options);

    private sealed record WireInbox(int Version, RuntimeChildControlTarget Target, long? NextOrdinal,
        bool? IsTerminalConsumed, IReadOnlyList<RuntimeChildControlTransport>? Pending);
}
