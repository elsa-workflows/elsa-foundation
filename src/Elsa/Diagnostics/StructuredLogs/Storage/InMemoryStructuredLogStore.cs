using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Storage;

/// <summary>
/// The default in-memory store: a bounded ring buffer for recent history plus an independent bounded
/// channel per live subscriber. A slow subscriber never blocks the logging path; its overflowed entries
/// are dropped and a <see cref="DroppedEntriesSignal"/> is surfaced in-band on the reader side.
/// </summary>
public sealed class InMemoryStructuredLogStore : IStructuredLogStore, IStructuredLogSink, IStructuredLogLiveFeed
{
    private readonly int _bufferCapacity;
    private readonly int _subscriberQueueCapacity;
    private readonly int _maxRecentQuerySize;
    private readonly object _gate = new();
    private readonly Queue<StructuredLogEntry> _buffer;
    private readonly List<Subscriber> _subscribers = [];
    private long _sequence;

    public InMemoryStructuredLogStore(IOptions<StructuredLogsOptions> options)
    {
        var value = options.Value;
        _bufferCapacity = Math.Max(1, value.BufferCapacity);
        _subscriberQueueCapacity = Math.Max(1, value.SubscriberQueueCapacity);
        _maxRecentQuerySize = Math.Max(1, value.MaxRecentQuerySize);
        _buffer = new Queue<StructuredLogEntry>(_bufferCapacity);
    }

    /// <inheritdoc />
    public void Emit(StructuredLogEntry entry) => Append(entry);

    /// <inheritdoc />
    public void Append(StructuredLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Subscriber[] snapshot;
        StructuredLogEntry stamped;

        lock (_gate)
        {
            stamped = entry with { Sequence = ++_sequence };
            _buffer.Enqueue(stamped);
            while (_buffer.Count > _bufferCapacity)
                _buffer.Dequeue();

            snapshot = _subscribers.ToArray();
        }

        foreach (var subscriber in snapshot)
            subscriber.TryDeliver(stamped);
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

    /// <inheritdoc />
    public IAsyncEnumerable<StructuredLogStreamItem> Subscribe(
        StructuredLogFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // Register eagerly (not inside the iterator) so entries appended before the first MoveNext are delivered.
        var subscriber = new Subscriber(filter, _subscriberQueueCapacity);
        lock (_gate)
            _subscribers.Add(subscriber);

        return Enumerate(subscriber, cancellationToken);
    }

    private async IAsyncEnumerable<StructuredLogStreamItem> Enumerate(
        Subscriber subscriber,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in subscriber.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            lock (_gate)
                _subscribers.Remove(subscriber);
        }
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

    /// <summary>
    /// A single live subscription. The producer writes matching entries into a bounded channel and, when
    /// it overflows, increments a drop counter instead of blocking. The reader drains entries and surfaces
    /// the accumulated drop count as an in-band signal.
    /// </summary>
    private sealed class Subscriber
    {
        private readonly StructuredLogFilter _filter;
        private readonly Channel<StructuredLogEntry> _channel;
        private long _droppedCount;
        private long _signalledDrops;
        private long _firstDropTicks;

        public Subscriber(StructuredLogFilter filter, int capacity)
        {
            _filter = filter;
            _channel = Channel.CreateBounded<StructuredLogEntry>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public void TryDeliver(StructuredLogEntry entry)
        {
            if (!_filter.Matches(entry))
                return;

            if (_channel.Writer.TryWrite(entry))
                return;

            if (Interlocked.Increment(ref _droppedCount) == 1)
                Interlocked.Exchange(ref _firstDropTicks, DateTimeOffset.UtcNow.UtcTicks);
        }

        public async IAsyncEnumerable<StructuredLogStreamItem> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (_channel.Reader.TryRead(out var entry))
                        yield return StructuredLogStreamItem.ForEntry(entry);

                    if (TryTakeDropSignal(out var dropped))
                        yield return StructuredLogStreamItem.ForDropped(dropped);
                }
            }
            finally
            {
                _channel.Writer.TryComplete();
            }
        }

        private bool TryTakeDropSignal(out DroppedEntriesSignal signal)
        {
            var dropped = Interlocked.Read(ref _droppedCount);
            if (dropped > _signalledDrops)
            {
                _signalledDrops = dropped;
                var since = new DateTimeOffset(Interlocked.Read(ref _firstDropTicks), TimeSpan.Zero);
                signal = new DroppedEntriesSignal(dropped, since);
                return true;
            }

            signal = default!;
            return false;
        }
    }
}
