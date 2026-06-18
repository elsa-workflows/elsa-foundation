using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Core.Contracts;

/// <summary>
/// Stores captured entries and answers recent-history queries. The default implementation is a bounded
/// in-memory ring buffer; replace it to persist entries (see EXTENSION_POINTS.md).
/// </summary>
public interface IStructuredLogStore
{
    /// <summary>Appends an already-sequenced entry to durable history.</summary>
    void Append(StructuredLogEntry entry);

    /// <summary>
    /// Returns the highest <see cref="StructuredLogEntry.Sequence"/> currently retained, or 0 when empty.
    /// Used to seed the sink's sequence counter so a persistent backend keeps increasing across restarts.
    /// </summary>
    long GetHighWaterMark();

    /// <summary>Returns the most-recent entries matching <paramref name="filter"/>, newest last.</summary>
    IReadOnlyList<StructuredLogEntry> GetRecent(StructuredLogFilter filter);

    /// <summary>
    /// Returns buffered entries with a sequence greater than <paramref name="afterSequence"/> matching
    /// <paramref name="filter"/>, oldest first. Used to resume a dropped live connection (Last-Event-ID).
    /// </summary>
    IReadOnlyList<StructuredLogEntry> GetAfter(long afterSequence, StructuredLogFilter filter);
}
