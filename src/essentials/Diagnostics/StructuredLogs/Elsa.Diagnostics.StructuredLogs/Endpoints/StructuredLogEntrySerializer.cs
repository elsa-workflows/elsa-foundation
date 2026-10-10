using Elsa.Diagnostics.StructuredLogs.Core.Models;
using System.Text.Json;

namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

/// <summary>
/// Serializes diagnostics entities to the wire JSON shape used by the <c>recent</c> response and the SSE
/// <c>data</c> lines. Kept as a standalone unit so the JSON contract can be branch-tested without spinning
/// up an HTTP host.
/// </summary>
public sealed class StructuredLogEntrySerializer
{
    /// <summary>Serializes a single entry to its JSON object form.</summary>
    public string Serialize(StructuredLogEntry entry) =>
        JsonSerializer.Serialize(entry, StructuredLogsJsonContext.Default.StructuredLogEntry);

    /// <summary>Serializes a sequence of entries to a JSON array.</summary>
    public string SerializeMany(IReadOnlyList<StructuredLogEntry> entries) =>
        JsonSerializer.Serialize(entries.ToArray(), StructuredLogsJsonContext.Default.StructuredLogEntryArray);

    /// <summary>Serializes a drop signal to its JSON object form.</summary>
    public string SerializeDropped(DroppedEntriesSignal signal) =>
        JsonSerializer.Serialize(signal, StructuredLogsJsonContext.Default.DroppedEntriesSignal);
}
