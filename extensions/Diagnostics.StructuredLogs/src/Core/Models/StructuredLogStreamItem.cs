namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// The envelope yielded by the live feed so log entries and backpressure drop signals travel on one
/// ordered stream. Exactly one of <see cref="Entry"/> or <see cref="Dropped"/> is non-null.
/// </summary>
public sealed record StructuredLogStreamItem
{
    private StructuredLogStreamItem(StructuredLogEntry? entry, DroppedEntriesSignal? dropped)
    {
        Entry = entry;
        Dropped = dropped;
    }

    /// <summary>Set when this item carries a log entry.</summary>
    public StructuredLogEntry? Entry { get; }

    /// <summary>Set when this item carries a backpressure drop notice.</summary>
    public DroppedEntriesSignal? Dropped { get; }

    /// <summary>True when this item carries a log entry.</summary>
    public bool IsEntry => Entry is not null;

    /// <summary>True when this item carries a drop notice.</summary>
    public bool IsDropped => Dropped is not null;

    /// <summary>Creates an item carrying a log entry.</summary>
    public static StructuredLogStreamItem ForEntry(StructuredLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(entry, null);
    }

    /// <summary>Creates an item carrying a drop notice.</summary>
    public static StructuredLogStreamItem ForDropped(DroppedEntriesSignal dropped)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        return new(null, dropped);
    }
}
