using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests;

/// <summary>
/// Covers the prune-resiliency half of issue #403: <see cref="EfCoreStructuredLogStore"/> must retry a
/// transiently failing prune (parity with <c>EfCoreOpenTelemetryStore.MaybePruneAsync</c>) instead of
/// resetting its insert counter up front and silently abandoning retention. Faults are injected
/// deterministically by failing selected drain-loop <c>CreateDbContextAsync</c> calls; the create
/// sequence is single-threaded (one drain loop), so call ordinals are stable.
/// </summary>
public sealed class EfCoreStructuredLogStorePruneRetryTests : IDisposable
{
    private readonly StructuredLogsTestHost _host = StructuredLogsTestHost.Create();
    private readonly FaultInjectingFactory _factory;

    public EfCoreStructuredLogStorePruneRetryTests()
    {
        _factory = new(_host);
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task SingleTransientPruneFailureDoesNotAbandonRetention()
    {
        // Drain-loop creates: #1 persists the batch, #2 is the first prune attempt — fail it once.
        _factory.FailAsyncCreateWhen = n => n == 2;

        var store = new EfCoreStructuredLogStore(_factory, Options.Create(new StructuredLogsOptions()), maxRetainedEntries: 2, pruneInterval: 1);

        // Enqueue everything before draining so the loop reads one deterministic batch of 10.
        for (var i = 1; i <= 10; i++)
            store.Append(TestEntries.Create(sequence: i, message: $"m{i}"));

        store.StartDraining();

        // Old behavior: the counter was already reset and there is no retry, so the failed prune is
        // abandoned permanently and all 10 rows survive. Fixed behavior: the prune retries and the
        // table shrinks to the retention cap.
        var pruned = await WaitForConditionAsync(() => CountRows() == 2, timeoutMs: 15_000);

        Assert.True(pruned, $"Expected the table to be pruned to 2 rows, found {CountRows()}.");
        Assert.Equal(new[] { 9L, 10L }, RowSequences());

        store.Dispose();
    }

    [Fact]
    public async Task ExhaustedPruneRetriesKeepTheCounterArmedForTheNextBatch()
    {
        // Creates #2..#7 cover the full retry budget of the first prune cycle (initial attempt + 5
        // retries) — exhaust it. Everything else succeeds.
        _factory.FailAsyncCreateWhen = n => n is >= 2 and <= 7;

        var store = new EfCoreStructuredLogStore(_factory, Options.Create(new StructuredLogsOptions()), maxRetainedEntries: 2, pruneInterval: 2);

        for (var i = 1; i <= 3; i++)
            store.Append(TestEntries.Create(sequence: i, message: $"m{i}"));

        store.StartDraining();

        // Wait for batch 1 to be persisted (create #1), then feed one more entry as a second batch.
        Assert.True(await WaitForConditionAsync(() => CountRows() == 3, timeoutMs: 15_000), "Batch 1 was never persisted.");
        store.Append(TestEntries.Create(sequence: 4, message: "m4"));

        // Fixed behavior: the exhausted prune cycle leaves the insert counter armed, so batch 2 pushes
        // it past the interval again and the (now healthy) prune brings the table down to the cap.
        // Old behavior: the counter was reset before the failed attempt, batch 2 only re-arms it to 1
        // (< interval 2), and no prune ever runs again.
        var pruned = await WaitForConditionAsync(() => CountRows() == 2, timeoutMs: 30_000);

        Assert.True(pruned, $"Expected the table to be pruned to 2 rows, found {CountRows()}.");
        Assert.Equal(new[] { 3L, 4L }, RowSequences());

        store.Dispose();
    }

    private int CountRows()
    {
        using var db = _host.CreateDbContext();
        return db.StructuredLogEntries.Count();
    }

    private long[] RowSequences()
    {
        using var db = _host.CreateDbContext();
        return db.StructuredLogEntries.OrderBy(x => x.Sequence).Select(x => x.Sequence).ToArray();
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> probe, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (probe())
                return true;
            await Task.Delay(25);
        }

        return probe();
    }

    /// <summary>
    /// Delegates to <see cref="StructuredLogsTestHost"/> but can fail designated *async* creates (the
    /// drain/prune path); the store's synchronous query creates and the test's own probes are untouched.
    /// </summary>
    private sealed class FaultInjectingFactory(StructuredLogsTestHost host) : IDbContextFactory<StructuredLogsDbContext>
    {
        private int _asyncCreates;

        public Func<int, bool>? FailAsyncCreateWhen { get; set; }

        public StructuredLogsDbContext CreateDbContext() => host.CreateDbContext();

        public Task<StructuredLogsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            var ordinal = Interlocked.Increment(ref _asyncCreates);

            if (FailAsyncCreateWhen?.Invoke(ordinal) == true)
                throw new InvalidOperationException($"Injected transient failure on async create #{ordinal}.");

            return Task.FromResult(host.CreateDbContext());
        }
    }
}
