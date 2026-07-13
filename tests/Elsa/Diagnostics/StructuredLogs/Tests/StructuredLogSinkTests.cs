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
        private long _cursorHighWater;

        public ConcurrentQueue<StructuredLogEntry> Appended { get; } = [];
        public int HighWaterMarkReads => _highWaterMarkReads;

        public ValueTask<StructuredLogEntry> AppendAsync(StructuredLogEntry entry, CancellationToken cancellationToken = default)
        {
            Appended.Enqueue(entry);
            return ValueTask.FromResult(entry with
            {
                ReplayCursor = new StructuredLogReplayCursor($"slrc1.test.{Interlocked.Increment(ref _cursorHighWater)}")
            });
        }

        public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _highWaterMarkReads);
            return Task.FromResult(highWaterMark);
        }

        public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StructuredLogEntry>>(Appended.ToArray());

        public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<StructuredLogReplayCursor?>(null);

        public Task<StructuredLogReadPage> ReadAfterAsync(StructuredLogReplayCursor? afterCursor, StructuredLogFilter filter, int maxCount, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StructuredLogReadPage([], afterCursor, false));

        public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default) => Task.CompletedTask;

    }

    private sealed class FakePublisher : IStructuredLogLivePublisher
    {
        public List<StructuredLogEntry> Published { get; } = [];
        public void Publish(StructuredLogEntry entry) => Published.Add(entry);
    }

    private sealed class DelayedCompletionStore : IStructuredLogStore
    {
        private readonly List<TaskCompletionSource<StructuredLogEntry>> _commits = [];

        public int AcceptedCount => _commits.Count;

        public ValueTask<StructuredLogEntry> AppendAsync(StructuredLogEntry entry, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<StructuredLogEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
            _commits.Add(completion);
            return new(completion.Task);
        }

        public void Complete(int index, string cursor) =>
            _commits[index].SetResult(TestEntries.Create(message: $"entry-{index + 1}") with
            {
                ReplayCursor = new StructuredLogReplayCursor(cursor)
            });

        public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);

        public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StructuredLogEntry>>([]);

        public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<StructuredLogReplayCursor?>(null);

        public Task<StructuredLogReadPage> ReadAfterAsync(StructuredLogReplayCursor? afterCursor, StructuredLogFilter filter, int maxCount, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StructuredLogReadPage([], afterCursor, false));

        public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
        Assert.Equal(appended with { ReplayCursor = published.ReplayCursor }, published);
        Assert.NotNull(published.ReplayCursor);
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

    [Fact]
    public async Task DelayedAppendCompletionsPublishInAcceptedDurableOrder()
    {
        var store = new DelayedCompletionStore();
        var publisher = new FakePublisher();
        var sink = new StructuredLogSink(store, publisher);

        sink.Emit(TestEntries.Create(message: "first"));
        sink.Emit(TestEntries.Create(message: "second"));
        Assert.Equal(2, store.AcceptedCount);

        store.Complete(1, "slrc1.test.cursor-2");
        await Task.Yield();
        Assert.Empty(publisher.Published);

        store.Complete(0, "slrc1.test.cursor-1");
        await WaitUntilAsync(() => publisher.Published.Count == 2);

        Assert.Equal(
            ["slrc1.test.cursor-1", "slrc1.test.cursor-2"],
            publisher.Published.Select(x => x.ReplayCursor!.Value.Value));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate())
            await Task.Delay(10, cts.Token);
    }
}
