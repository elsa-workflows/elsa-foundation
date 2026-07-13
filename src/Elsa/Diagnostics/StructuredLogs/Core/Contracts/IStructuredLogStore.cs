using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Core.Contracts;

/// <summary>
/// Stores captured entries and answers recent-history queries. The default implementation is a bounded
/// in-memory ring buffer; replace it to persist entries (see EXTENSION_POINTS.md). Queries are async so
/// persistent backends never block a request thread on storage I/O (issue #411). The capture sink never
/// waits for <see cref="AppendAsync"/>; durable adapters own their bounded background queues and complete
/// the returned operation only after the append commits.
/// </summary>
public interface IStructuredLogStore
{
    /// <summary>
    /// Accepts an entry for append and returns the committed representation carrying its authoritative
    /// replay cursor. The same accepted append retried after acknowledgement loss must return the same
    /// cursor. A caller must not publish the entry to a durable live feed before this operation completes.
    /// </summary>
    ValueTask<StructuredLogEntry> AppendAsync(
        StructuredLogEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the lifetime maximum committed <see cref="StructuredLogEntry.Sequence"/>, or 0 only when
    /// the bound stream has never committed an entry. Retention must never rewind this value.
    /// </summary>
    Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the most-recent entries matching <paramref name="filter"/>, newest last.</summary>
    Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Returns the cursor of the newest retained committed entry, or <c>null</c> when empty.</summary>
    Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads at most <paramref name="maxCount"/> committed cursor positions strictly after
    /// <paramref name="afterCursor"/> from one provider snapshot. The provider must advance
    /// <see cref="StructuredLogReadPage.NextCursor"/> across every scanned position, including records
    /// excluded by <paramref name="filter"/>. This makes repeated calls a gap-free, globally ordered tail.
    /// A <c>null</c> cursor starts at the oldest retained position. Invalid, expired, trimmed, wrong-scope
    /// and wrong-stream cursors fail with the same non-disclosing exception.
    /// </summary>
    Task<StructuredLogReadPage> ReadAfterAsync(
        StructuredLogReplayCursor? afterCursor,
        StructuredLogFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retains only the newest <paramref name="keepNewest"/> entries in committed cursor order. A value
    /// of zero removes every retained entry while preserving lifetime cursor and logical high-water state.
    /// </summary>
    Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default);
}
