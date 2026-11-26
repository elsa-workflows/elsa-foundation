using System.Collections.Concurrent;
using Elsa.Diagnostics.StructuredLogs.Capture;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Sources;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogSinkTests
{
    private sealed class FakeStore : IStructuredLogStore
    {
        private long _cursorHighWater;

        public ConcurrentQueue<StructuredLogEntry> Appended { get; } = [];

        public ValueTask<StructuredLogEntry> AppendAsync(StructuredLogEntry entry, CancellationToken cancellationToken = default)
        {
            Appended.Enqueue(entry);
            return ValueTask.FromResult(entry with
            {
                ReplayCursor = new StructuredLogReplayCursor($"slrc1.test.{Interlocked.Increment(ref _cursorHighWater)}")
            });
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

    /// <summary>
    /// A durable store while its shell activates: migrations have not created the schema yet, so every read and
    /// append fails the way SQLite does, until <see cref="CompleteMigration"/> runs.
    /// </summary>
    private sealed class MigratingStore : IStructuredLogStore
    {
        private int _ready;

        public ConcurrentQueue<StructuredLogEntry> Persisted { get; } = [];

        public void CompleteMigration() => Volatile.Write(ref _ready, 1);

        public ValueTask<StructuredLogEntry> AppendAsync(StructuredLogEntry entry, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _ready) == 0)
                return ValueTask.FromException<StructuredLogEntry>(NoSuchTable("elsa_structured_log_records"));

            Persisted.Enqueue(entry);
            return ValueTask.FromResult(entry with { ReplayCursor = new StructuredLogReplayCursor($"slrc1.test.{Persisted.Count}") });
        }

        public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StructuredLogEntry>>(Persisted.ToArray());

        public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<StructuredLogReplayCursor?>(null);

        public Task<StructuredLogReadPage> ReadAfterAsync(StructuredLogReplayCursor? afterCursor, StructuredLogFilter filter, int maxCount, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StructuredLogReadPage([], afterCursor, false));

        public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static InvalidOperationException NoSuchTable(string table) => new($"SQLite Error 1: 'no such table: {table}'.");
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

        public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StructuredLogEntry>>([]);

        public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<StructuredLogReplayCursor?>(null);

        public Task<StructuredLogReadPage> ReadAfterAsync(StructuredLogReplayCursor? afterCursor, StructuredLogFilter filter, int maxCount, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StructuredLogReadPage([], afterCursor, false));

        public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public void EmitStampsMonotonicDisplaySequences()
    {
        var store = new FakeStore();
        var sink = new StructuredLogSink(store, new FakePublisher());

        sink.Emit(TestEntries.Create());
        sink.Emit(TestEntries.Create());

        Assert.Equal(new[] { 1L, 2L }, store.Appended.Select(e => e.Sequence));
    }

    [Fact]
    public void EmitAppendsToStoreAndPublishesTheSameStampedEntry()
    {
        var store = new FakeStore();
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
    public async Task ConcurrentEmitsStampDistinctDisplaySequences()
    {
        const int emitters = 16;
        var store = new FakeStore();
        var sink = new StructuredLogSink(store, new FakePublisher());

        using var startGate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, emitters).Select(_ => Task.Run(() =>
        {
            startGate.Wait();
            sink.Emit(TestEntries.Create());
        })).ToArray();
        startGate.Set();
        await Task.WhenAll(tasks);

        var sequences = store.Appended.Select(e => e.Sequence).OrderBy(s => s).ToArray();
        Assert.Equal(Enumerable.Range(1, emitters).Select(i => (long)i), sequences);
    }

    /// <summary>
    /// A shell logs while it activates, before its migrations create the structured-log tables, so the store fails
    /// every read and append at first. Capture must come back once the store is ready: a failure at the first log
    /// line used to be cached for the life of the process, and every later entry was silently discarded. The entries
    /// logged before readiness are dropped, never replayed, and do not hold back the ones after.
    /// </summary>
    [Fact]
    public async Task Captures_logged_after_the_store_becomes_ready_are_persisted_when_earlier_captures_failed()
    {
        var store = new MigratingStore();
        var publisher = new FakePublisher();
        var logger = new StructuredLogCaptureProvider(new StructuredLogSink(store, publisher), new LocalStructuredLogSourceProvider(), TestOptions.Create())
            .CreateLogger("Shell.Activation");

        logger.LogInformation("before-migration-1");
        logger.LogInformation("before-migration-2");
        store.CompleteMigration();
        logger.LogInformation("after-migration-1");
        logger.LogInformation("after-migration-2");

        Assert.Equal(["after-migration-1", "after-migration-2"], store.Persisted.Select(entry => entry.Message));
        await WaitUntilAsync(() => publisher.Published.Count == 2);
        Assert.Equal(["after-migration-1", "after-migration-2"], publisher.Published.Select(entry => entry.Message));
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
