using System.Text;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

/// <summary>
/// Formats <see cref="StructuredLogStreamItem"/> values into Server-Sent Events frames. Pure string
/// production so the SSE wire contract (id / event / data framing) can be branch-tested without an HTTP host.
/// </summary>
public sealed class StructuredLogSseFormatter
{
    private readonly StructuredLogEntrySerializer _serializer;

    public StructuredLogSseFormatter(StructuredLogEntrySerializer serializer) => _serializer = serializer;

    /// <summary>
    /// Formats a stream item into an SSE frame. Entries carry an <c>id</c> (their committed cursor) and the
    /// <c>entry</c> event; drop signals carry the <c>dropped</c> event with no id.
    /// </summary>
    public string Format(StructuredLogStreamItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Entry is { } entry)
            return FormatEntry(entry);

        if (item.Dropped is { } dropped)
            return FormatDropped(dropped);

        throw new ArgumentException("Stream item carries neither an entry nor a drop signal.", nameof(item));
    }

    /// <summary>Formats a single entry as an <c>id</c>/<c>event: entry</c> SSE frame.</summary>
    public string FormatEntry(StructuredLogEntry entry)
    {
        if (entry.ReplayCursor is not { IsValid: true } cursor)
            throw new ArgumentException("Only committed structured log entries can be formatted as SSE events.", nameof(entry));

        var builder = new StringBuilder();
        builder.Append("id: ").Append(cursor.Value).Append('\n');
        builder.Append("event: entry\n");
        builder.Append("data: ").Append(_serializer.Serialize(entry)).Append("\n\n");
        return builder.ToString();
    }

    /// <summary>Formats a drop signal as an <c>event: dropped</c> SSE frame.</summary>
    public string FormatDropped(DroppedEntriesSignal dropped)
    {
        var builder = new StringBuilder();
        builder.Append("event: dropped\n");
        builder.Append("data: ").Append(_serializer.SerializeDropped(dropped)).Append("\n\n");
        return builder.ToString();
    }

    /// <summary>Returns the SSE comment heartbeat that keeps idle connections open.</summary>
    public string Heartbeat() => ": keep-alive\n\n";
}
