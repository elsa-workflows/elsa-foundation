using System.Collections.Concurrent;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogSinkTests
{
    private sealed class FakeStore(long highWaterMark) : IStructuredLogStore
    {
        private int _highWaterMarkReads;

        public ConcurrentQueue<StructuredLogEntry> Appended { get; } = [];
        public int HighWaterMarkReads => _highWaterMarkReads;

        public void Append(StructuredLogEntry entry) => Appended.Enqueue(entry);

        public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _highWaterMarkReads);
            return Task.FromResult(highWaterMark);
        }

        public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StructuredLogEntry>>(Appended.ToArray());

        public Task<IReadOnlyList<StructuredLogEntry>> GetAfterAsync(long afterSequence, StructuredLogFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StructuredLogEntry>>(Appended.ToArray());
    }

    private sealed class FakePublisher : IStructuredLogLivePublisher
    {
        public List<StructuredLogEntry> Published { get; } = [];
        public void Publish(StructuredLogEntry entry) => Published.Add(entry);
    }

    [Fact]
    public void EmitSeedsSequenceFromStoreHighWaterMarkAndIncrementsMonotonically()
    {
        var store = new FakeStore(highWaterMark: 10);
        var sink = new StructuredLogSink(store, new FakePublisher());

        sink.Emit(TestEntries.Create());
        sink.Emit(TestEntries.Create());

        Assert.Equal(new[] { 11L, 12L }, store.Appended.Select(e => e.Sequence));
    }

    [Fact]
    public void EmitAppendsToStoreAndPublishesTheSameStampedEntry()
    {
        var store = new FakeStore(highWaterMark: 0);
        var publisher = new FakePublisher();
        var sink = new StructuredLogSink(store, publisher);

        sink.Emit(TestEntries.Create(message: "x"));

        var appended = Assert.Single(store.Appended);
        var published = Assert.Single(publisher.Published);
        Assert.Equal(1L, appended.Sequence);
        Assert.Same(appended, published);
    }

    [Fact]
    public void ConstructionDoesNotTouchTheStore()
    {
        var store = new FakeStore(highWaterMark: 10);

        _ = new StructuredLogSink(store, new FakePublisher());

        Assert.Equal(0, store.HighWaterMarkReads);
    }

    /// <summary>
    /// Issue #411 follow-up requirement: seeding is lazy (off the constructor), but it must complete
    /// before the first emitted sequence is assigned — even when the first emits race — and must run
    /// exactly once. No emitted sequence may ever be at or below the store's high-water mark.
    /// </summary>
    [Fact]
    public async Task ConcurrentFirstEmitsSeedExactlyOnceAndNeverStampAtOrBelowHighWaterMark()
    {
        const long highWaterMark = 100;
        const int emitters = 16;
        var store = new FakeStore(highWaterMark);
        var sink = new StructuredLogSink(store, new FakePublisher());

        using var startGate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, emitters).Select(_ => Task.Run(() =>
        {
            startGate.Wait();
            sink.Emit(TestEntries.Create());
        })).ToArray();
        startGate.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(1, store.HighWaterMarkReads);
        var sequences = store.Appended.Select(e => e.Sequence).OrderBy(s => s).ToArray();
        Assert.Equal(Enumerable.Range(1, emitters).Select(i => highWaterMark + i), sequences);
    }
}
