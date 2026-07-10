using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests;

/// <summary>
/// Covers the observability half of issue #607 (parity with <c>EfCoreStructuredLogStore</c>): batches
/// dropped after retry exhaustion and batches shed by the bounded channel must be logged and surface in
/// the <c>Dropped*Count</c> diagnostics instead of disappearing silently, and a dropped batch must not
/// kill the drain loop.
/// </summary>
public sealed class EfCoreOpenTelemetryStoreResilienceTests : IDisposable
{
    private readonly OpenTelemetryTestHost _host = OpenTelemetryTestHost.Create();
    private readonly CapturingLogger<EfCoreOpenTelemetryStore> _logger = new();
    private readonly OpenTelemetryDiagnosticsOptions _options = new() { MaxQuerySize = 10 };

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ExhaustedPersistRetriesDropTheBatchLogAndSurfaceDroppedCounts()
    {
        // Creates #1..#9 exhaust the full persist retry budget (initial attempt + 8 retries) of the
        // first batch; everything afterwards succeeds.
        var factory = new FaultInjectingFactory(_host) { FailAsyncCreateWhen = n => n <= 9 };
        var registry = new OpenTelemetrySourceRegistry(Microsoft.Extensions.Options.Options.Create(_options));
        using var store = new EfCoreOpenTelemetryStore(factory, Microsoft.Extensions.Options.Options.Create(_options), registry, pruneInterval: 500, _logger, baseRetryDelay: TimeSpan.FromMilliseconds(1));

        await store.WriteAsync(DoomedBatch("trace-doomed"));
        store.StartDraining();

        var dropped = await WaitForConditionAsync(() => Task.FromResult(_logger.Snapshot().Any(x => x.Level == LogLevel.Error && x.Message.Contains("Dropping a telemetry batch"))));
        Assert.True(dropped, "Expected an error log for the dropped batch.");

        // The loop must survive the drop: a subsequent batch persists normally.
        await store.WriteAsync(DoomedBatch("trace-survivor"));
        var persisted = await WaitForConditionAsync(async () => (await store.GetDiagnosticsAsync()).TraceCount == 1);
        Assert.True(persisted, "Expected the follow-up batch to persist.");

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal(1, diagnostics.DroppedTraceCount);
        Assert.Equal(1, diagnostics.DroppedSpanCount);
        Assert.Equal(1, diagnostics.DroppedMetricPointCount);
        Assert.Equal(1, diagnostics.DroppedLogRecordCount);
    }

    [Fact]
    public async Task ChannelOverflowShedsOldestBatchesLogsAndSurfacesDroppedCounts()
    {
        // SubscriberChannelCapacity 1 clamps the channel to its minimum of BatchSize (64) * 4 = 256
        // batches. With the drain loop never started, batch 257 forces the channel to shed the oldest.
        var options = new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 10, SubscriberChannelCapacity = 1 };
        var registry = new OpenTelemetrySourceRegistry(Microsoft.Extensions.Options.Options.Create(options));
        using var store = new EfCoreOpenTelemetryStore(_host, Microsoft.Extensions.Options.Options.Create(options), registry, logger: _logger);

        for (var i = 1; i <= 257; i++)
        {
            var resource = TestModels.Resource($"resource-{i}", "api");
            await store.WriteAsync(new OpenTelemetryBatch([], [TestModels.Trace($"trace-{i}", resource.Id)], [], [], [], []));
        }

        var warning = Assert.Single(_logger.Snapshot(), x => x.Level == LogLevel.Warning);
        Assert.Contains("shedding the oldest queued batch", warning.Message);

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal(1, diagnostics.DroppedTraceCount);
    }

    private OpenTelemetryBatch DoomedBatch(string traceId)
    {
        var resource = TestModels.Resource($"resource-{traceId}", "api");
        var trace = TestModels.Trace(traceId, resource.Id);
        var span = TestModels.Span($"span-record-{traceId}", trace.TraceId, $"span-{traceId}", resource.Id);
        var instrument = TestModels.Instrument($"instrument-{traceId}", resource.Id, "duration");
        var point = TestModels.Point($"point-{traceId}", instrument.Id, resource.Id);
        var log = TestModels.Log($"log-{traceId}", resource.Id, trace.TraceId);
        return new OpenTelemetryBatch([resource], [trace], [span], [instrument], [point], [log]);
    }

    private static async Task<bool> WaitForConditionAsync(Func<Task<bool>> probe, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await probe())
                return true;
            await Task.Delay(25);
        }

        return await probe();
    }

    /// <summary>Delegates to the host but fails designated async creates (the drain path).</summary>
    private sealed class FaultInjectingFactory(OpenTelemetryTestHost host) : IDbContextFactory<OpenTelemetryDbContext>
    {
        private int _asyncCreates;

        public Func<int, bool>? FailAsyncCreateWhen { get; set; }

        public OpenTelemetryDbContext CreateDbContext() => host.CreateDbContext();

        public Task<OpenTelemetryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            var ordinal = Interlocked.Increment(ref _asyncCreates);

            if (FailAsyncCreateWhen?.Invoke(ordinal) == true)
                throw new InvalidOperationException($"Injected transient failure on async create #{ordinal}.");

            return Task.FromResult(host.CreateDbContext());
        }
    }
}
