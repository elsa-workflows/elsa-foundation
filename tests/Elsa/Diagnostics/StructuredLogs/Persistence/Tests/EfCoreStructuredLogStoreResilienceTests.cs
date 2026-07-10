using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests;

/// <summary>
/// Covers the observability half of issue #607 (parity with <c>EfCoreOpenTelemetryStore</c>): a batch
/// dropped after retry exhaustion and entries shed by the bounded channel must be logged instead of
/// disappearing silently, and a dropped batch must not kill the drain loop.
/// </summary>
public sealed class EfCoreStructuredLogStoreResilienceTests : IDisposable
{
    private readonly StructuredLogsTestHost _host = StructuredLogsTestHost.Create();
    private readonly CapturingLogger<EfCoreStructuredLogStore> _logger = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ExhaustedPersistRetriesLogTheDroppedBatchAndKeepTheDrainLoopAlive()
    {
        // Creates #1..#9 exhaust the full persist retry budget (initial attempt + 8 retries) of the
        // first batch; everything afterwards succeeds.
        var factory = new FaultInjectingFactory(_host) { FailAsyncCreateWhen = n => n <= 9 };
        using var store = new EfCoreStructuredLogStore(factory, Options.Create(new StructuredLogsOptions()), maxRetainedEntries: 100, pruneInterval: 100, _logger, baseRetryDelay: TimeSpan.FromMilliseconds(1));

        store.Append(TestEntries.Create(sequence: 1, message: "doomed"));
        store.StartDraining();

        var dropped = await WaitForConditionAsync(() => _logger.Snapshot().Any(x => x.Level == LogLevel.Error && x.Message.Contains("Dropping a structured log batch")));
        Assert.True(dropped, "Expected an error log for the dropped batch.");

        // The loop must survive the drop: a subsequent batch persists normally.
        store.Append(TestEntries.Create(sequence: 2, message: "survivor"));
        var persisted = await WaitForConditionAsync(() => CountRows() == 1);
        Assert.True(persisted, $"Expected the follow-up batch to persist, found {CountRows()} rows.");

        var retries = _logger.Snapshot().Count(x => x.Level == LogLevel.Debug && x.Message.Contains("Transient failure persisting"));
        Assert.Equal(8, retries);
    }

    [Fact]
    public void ChannelOverflowShedsOldestEntriesAndLogsAWarning()
    {
        // BufferCapacity 1 clamps the channel to its minimum of BatchSize (200) * 4 = 800 entries. With
        // the drain loop never started, entry 801 forces the channel to shed the oldest queued entry.
        using var store = new EfCoreStructuredLogStore(_host, Options.Create(new StructuredLogsOptions { BufferCapacity = 1 }), maxRetainedEntries: 100_000, pruneInterval: 5_000, _logger);

        for (var i = 1; i <= 801; i++)
            store.Append(TestEntries.Create(sequence: i, message: $"m{i}"));

        var warning = Assert.Single(_logger.Snapshot(), x => x.Level == LogLevel.Warning);
        Assert.Contains("shedding the oldest queued entry", warning.Message);
    }

    private int CountRows()
    {
        using var db = _host.CreateDbContext();
        return db.StructuredLogEntries.Count();
    }

    private static Task<bool> WaitForConditionAsync(Func<bool> probe, int timeoutMs = 10_000) =>
        TestWait.ForConditionAsync(probe, timeoutMs);
}
