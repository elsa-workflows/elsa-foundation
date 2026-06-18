using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
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
    private readonly Queue<StructuredLogEntry> _buffer;
    private long _highWaterMark;

    public InMemoryStructuredLogStore(IOptions<StructuredLogsOptions> options)
    {
        var value = options.Value;
        _bufferCapacity = Math.Max(1, value.BufferCapacity);
        _maxRecentQuerySize = Math.Max(1, value.MaxRecentQuerySize);
        _buffer = new Queue<StructuredLogEntry>(_bufferCapacity);
    }

    /// <inheritdoc />
    public void Append(StructuredLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            _buffer.Enqueue(entry);
            while (_buffer.Count > _bufferCapacity)
                _buffer.Dequeue();

            if (entry.Sequence > _highWaterMark)
                _highWaterMark = entry.Sequence;
        }
    }

    /// <inheritdoc />
    public long GetHighWaterMark()
    {
        lock (_gate)
            return _highWaterMark;
    }

    /// <inheritdoc />
    public IReadOnlyList<StructuredLogEntry> GetRecent(StructuredLogFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var max = filter.MaxCount is { } requested
            ? Math.Clamp(requested, 0, _maxRecentQuerySize)
            : _maxRecentQuerySize;

        if (max == 0)
            return [];

        var matched = SnapshotMatching(filter, afterSequence: null);
        if (matched.Count <= max)
            return matched;

        return matched.GetRange(matched.Count - max, max);
    }

    /// <inheritdoc />
    public IReadOnlyList<StructuredLogEntry> GetAfter(long afterSequence, StructuredLogFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return SnapshotMatching(filter, afterSequence);
    }

    private List<StructuredLogEntry> SnapshotMatching(StructuredLogFilter filter, long? afterSequence)
    {
        StructuredLogEntry[] snapshot;
        lock (_gate)
            snapshot = _buffer.ToArray();

        var result = new List<StructuredLogEntry>(snapshot.Length);
        foreach (var entry in snapshot)
        {
            if (afterSequence is { } after && entry.Sequence <= after)
                continue;
            if (filter.Matches(entry))
                result.Add(entry);
        }

        return result;
    }
}
