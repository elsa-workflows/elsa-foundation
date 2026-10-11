using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Live;

/// <summary>
/// The in-process live feed: an independent bounded channel per subscriber. A slow subscriber never
/// blocks the publisher; its overflowed entries are dropped and a <see cref="DroppedEntriesSignal"/> is
/// surfaced in-band on the reader side. This component owns only fan-out — sequence assignment and
/// durable history live elsewhere — so it is shared by every storage backend.
/// </summary>
public sealed class InMemoryStructuredLogLiveFeed : IStructuredLogLiveFeed, IStructuredLogLivePublisher
{
    private readonly int _subscriberQueueCapacity;
    private readonly object _gate = new();
    private readonly List<Subscriber> _subscribers = [];

    public InMemoryStructuredLogLiveFeed(IOptions<StructuredLogsOptions> options) =>
        _subscriberQueueCapacity = Math.Max(1, options.Value.SubscriberQueueCapacity);

    /// <inheritdoc />
    public void Publish(StructuredLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Subscriber[] snapshot;
        lock (_gate)
            snapshot = _subscribers.ToArray();

        foreach (var subscriber in snapshot)
            subscriber.TryDeliver(entry);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StructuredLogStreamItem> Subscribe(
        StructuredLogFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // Register eagerly (not inside the iterator) so entries published before the first MoveNext are delivered.
        var subscriber = new Subscriber(filter, _subscriberQueueCapacity);
        lock (_gate)
            _subscribers.Add(subscriber);

        var cancellationRegistration = cancellationToken.Register(() => Remove(subscriber));

        return Enumerate(subscriber, cancellationRegistration, cancellationToken);
    }

    private async IAsyncEnumerable<StructuredLogStreamItem> Enumerate(
        Subscriber subscriber,
        CancellationTokenRegistration cancellationRegistration,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in subscriber.ReadAllAsync(cancellationToken))
                yield return item;
        }
        finally
        {
            await cancellationRegistration.DisposeAsync();
            Remove(subscriber);
        }
    }

    private void Remove(Subscriber subscriber)
    {
        lock (_gate)
            _subscribers.Remove(subscriber);
    }

    /// <summary>
    /// A single live subscription. The publisher writes matching entries into a bounded channel and, when
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
                while (await _channel.Reader.WaitToReadAsync(cancellationToken))
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
