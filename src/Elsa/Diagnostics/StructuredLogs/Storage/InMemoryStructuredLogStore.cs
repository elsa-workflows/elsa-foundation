using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Storage;

/// <summary>
/// The default history store: a bounded in-memory ring buffer. Entries arrive already sequenced from
/// <see cref="StructuredLogSink"/>; this type only retains and queries them. Live fan-out is handled
/// separately by the live feed, so swapping this for a persistent store leaves the live tail unchanged.
/// </summary>
public sealed class InMemoryStructuredLogStore : IStructuredLogStore
{
    private readonly int _bufferCapacity;
    private readonly int _maxRecentQuerySize;
    private readonly object _gate = new();
    private readonly StructuredLogStoreBinding _binding;
    private readonly Queue<StoredEntry> _buffer;
    private long _highWaterMark;
    private long _cursorHighWater;

    public InMemoryStructuredLogStore(
        IOptions<StructuredLogsOptions> options,
        StructuredLogStoreBinding? binding = null)
    {
        var value = options.Value;
        _bufferCapacity = Math.Max(1, value.BufferCapacity);
        _maxRecentQuerySize = Math.Max(1, value.MaxRecentQuerySize);
        _binding = binding ?? StructuredLogStoreBinding.Default;
        _buffer = new Queue<StoredEntry>(_bufferCapacity);
    }

    /// <inheritdoc />
    public ValueTask<StructuredLogEntry> AppendAsync(
        StructuredLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var providerPosition = (++_cursorHighWater).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var recordToken = Guid.NewGuid().ToString("N");
            var committed = entry with
            {
                ReplayCursor = InMemoryReplayCursorCodec.Encode(
                    _binding,
                    entry.SourceId,
                    recordToken,
                    providerPosition)
            };
            _buffer.Enqueue(new(committed, recordToken, providerPosition));
            while (_buffer.Count > _bufferCapacity)
                _buffer.Dequeue();

            if (entry.Sequence > _highWaterMark)
                _highWaterMark = entry.Sequence;

            return ValueTask.FromResult(committed);
        }
    }

    /// <inheritdoc />
    public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_highWaterMark);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var max = filter.MaxCount is { } requested
            ? Math.Clamp(requested, 0, _maxRecentQuerySize)
            : _maxRecentQuerySize;

        if (max == 0)
            return Task.FromResult<IReadOnlyList<StructuredLogEntry>>([]);

        var matched = SnapshotMatching(filter);
        if (matched.Count > max)
            matched = matched.GetRange(matched.Count - max, max);

        return Task.FromResult<IReadOnlyList<StructuredLogEntry>>(matched);
    }

    /// <inheritdoc />
    public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_buffer.Count == 0 ? null : _buffer.Last().Entry.ReplayCursor);
    }

    /// <inheritdoc />
    public Task<StructuredLogReadPage> ReadAfterAsync(
        StructuredLogReplayCursor? afterCursor,
        StructuredLogFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        cancellationToken.ThrowIfCancellationRequested();
        var limit = Math.Min(maxCount, _maxRecentQuerySize);

        StructuredLogReadPage page;
        lock (_gate)
        {
            var snapshot = _buffer.ToArray();
            var start = -1;
            if (afterCursor is { } cursor)
            {
                if (!InMemoryReplayCursorCodec.TryDecode(cursor, _binding, out var parts))
                    throw new StructuredLogReplayCursorUnavailableException();
                start = Array.FindIndex(snapshot, x =>
                    StringComparer.Ordinal.Equals(x.RecordToken, parts.RecordToken) &&
                    StringComparer.Ordinal.Equals(x.ProviderPosition, parts.ProviderPosition) &&
                    StringComparer.Ordinal.Equals(x.Entry.SourceId, parts.EntrySourceId));
                if (start < 0)
                    throw new StructuredLogReplayCursorUnavailableException();
            }

            var scanned = snapshot.Skip(start + 1).Take(limit).ToArray();
            page = new(
                scanned.Select(x => x.Entry).Where(filter.Matches).ToArray(),
                scanned.Length == 0 ? afterCursor : scanned[^1].Entry.ReplayCursor,
                start + 1 + scanned.Length < snapshot.Length);
        }

        return Task.FromResult(page);
    }

    /// <inheritdoc />
    public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepNewest);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
            while (_buffer.Count > keepNewest)
                _buffer.Dequeue();

        return Task.CompletedTask;
    }

    private List<StructuredLogEntry> SnapshotMatching(StructuredLogFilter filter)
    {
        StoredEntry[] snapshot;
        lock (_gate)
            snapshot = _buffer.ToArray();

        var result = new List<StructuredLogEntry>(snapshot.Length);
        foreach (var item in snapshot)
        {
            var entry = item.Entry;
            if (filter.Matches(entry))
                result.Add(entry);
        }

        return result;
    }

    private sealed record StoredEntry(
        StructuredLogEntry Entry,
        string RecordToken,
        string ProviderPosition);
}
