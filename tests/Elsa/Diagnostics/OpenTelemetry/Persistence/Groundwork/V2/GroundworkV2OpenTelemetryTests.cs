using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Draining;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkV2OpenTelemetryTests
{
    [Fact]
    public void Public_declarations_are_ordinary_scoped_units_with_pre_source_aggregation()
    {
        var units = V2OpenTelemetryStorageSchema.CreateUnits();

        Assert.Equal(7, units.Count);
        Assert.All(units, unit => Assert.Equal(ScopePolicy.Scoped, unit.Scope));
        var traces = Assert.Single(units, unit => unit.Id.Value == V2OpenTelemetryStorageSchema.TraceUnitId);
        Assert.Equal(ColumnGeneration.ProviderSequence, Assert.Single(traces.Columns, column => column.Name == V2OpenTelemetryStorageSchema.Sequence).Generation);
        Assert.Contains(traces.AggregationProfiles, profile => profile.Name == V2OpenTelemetryStorageSchema.TraceProfile);
    }

    [Fact]
    public async Task SQLite_round_trip_uses_ordinary_units_and_declared_trace_source_filter()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-otel-v2-{Guid.NewGuid():N}.db");
        try
        {
            var connection = new SqliteProviderFactory().Create($"Data Source={path}");
            await using var store = new GroundworkOpenTelemetryStore(
                connection,
                Options.Create(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 100 }),
                V2OpenTelemetryBinding.Default);
            await using var lease = await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync();
            store.Start();

            var resource = new TelemetryResource("resource-1", "orders", null, "dotnet", new Dictionary<string, string?>(), DateTimeOffset.UtcNow, TelemetryResourceStatus.Active);
            var trace = new TelemetryTrace("trace-1", "root-1", "checkout", DateTimeOffset.UtcNow.AddSeconds(-2), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2), SpanStatus.Error, [resource.Id], ["workflow-1"], 1);
            var span = new TelemetrySpan("span-1", trace.TraceId, "span-1", null, resource.Id, "checkout", "server", trace.StartTime, trace.EndTime, SpanStatus.Error, null, new Dictionary<string, string?>(), [], []);
            await store.WriteAsync(DiagnosticsDrainBatchId.New(), new OpenTelemetryBatch([resource], [trace], [span], [], [], []));

            var result = await store.QueryTracesAsync(new OpenTelemetryTraceFilter { ServiceName = "orders", Status = SpanStatus.Error, Take = 10 });
            var detail = await store.GetTraceAsync(trace.TraceId);

            Assert.Equal([trace.TraceId], result.Items.Select(item => item.TraceId));
            Assert.Single(detail!.Spans);
            Assert.Equal(resource.Id, detail.Resources.Single().Id);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task SQLite_source_filter_is_applied_before_trace_reduction_and_exact_append_replays()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-otel-v2-{Guid.NewGuid():N}.db");
        try
        {
            var connection = new SqliteProviderFactory().Create($"Data Source={path}");
            await using var store = new GroundworkOpenTelemetryStore(
                connection,
                Options.Create(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 100 }),
                V2OpenTelemetryBinding.Default);
            await using var lease = await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync();
            store.Start();
            var now = DateTimeOffset.UtcNow;
            var firstResource = Resource("resource-a", "api");
            var secondResource = Resource("resource-b", "worker");
            var first = Trace("trace-shared", firstResource, now.AddSeconds(-2));
            var second = Trace("trace-shared", secondResource, now.AddSeconds(-1));
            var batch = new OpenTelemetryBatch([firstResource, secondResource], [first, second], [], [], [], []);
            var batchId = DiagnosticsDrainBatchId.New();

            await store.WriteAsync(batchId, batch);
            await store.WriteAsync(batchId, batch);

            var api = await store.QueryTracesAsync(new OpenTelemetryTraceFilter { ServiceName = "api", Take = 10 });
            var all = await store.QueryTracesAsync(new OpenTelemetryTraceFilter { Take = 10 });

            Assert.Equal(1, api.Items.Single().SpanCount);
            Assert.Equal(2, all.Items.Single().SpanCount);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        static TelemetryResource Resource(string id, string service) =>
            new(id, service, null, "dotnet", new Dictionary<string, string?>(), DateTimeOffset.UtcNow, TelemetryResourceStatus.Active);

        static TelemetryTrace Trace(string id, TelemetryResource resource, DateTimeOffset start) =>
            new(id, null, "operation", start, start.AddSeconds(1), TimeSpan.FromSeconds(1), SpanStatus.Ok, [resource.Id], [], 1);
    }

    [Fact]
    public async Task SQLite_queued_capture_survives_restart_and_scope_isolation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-otel-v2-{Guid.NewGuid():N}.db");
        try
        {
            var options = Options.Create(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 100 });
            var binding = new V2OpenTelemetryBinding("tenant", "scope", "collector");
            var batch = CaptureBatch("restart-trace");
            var firstConnection = new SqliteProviderFactory().Create($"Data Source={path}");
            try
            {
                var first = new GroundworkOpenTelemetryStore(firstConnection, options, binding);
                await using (first)
                {
                    await using var lease = await ((IDiagnosticsPersistenceStartupResource)first).AcquireAsync();
                    first.Start();
                    await first.WriteAsync(batch);
                    await first.CompleteDrainingAsync();
                }
            }
            finally { firstConnection.Dispose(); }

            var restartedConnection = new SqliteProviderFactory().Create($"Data Source={path}");
            try
            {
                var restarted = new GroundworkOpenTelemetryStore(restartedConnection, options, binding);
                await using (restarted)
                {
                    await using var lease = await ((IDiagnosticsPersistenceStartupResource)restarted).AcquireAsync();
                    Assert.Equal(["restart-trace"], (await restarted.QueryTracesAsync(new OpenTelemetryTraceFilter())).Items.Select(item => item.TraceId));
                }
            }
            finally { restartedConnection.Dispose(); }

            var foreignConnection = new SqliteProviderFactory().Create($"Data Source={path}");
            try
            {
                var foreign = new GroundworkOpenTelemetryStore(
                    foreignConnection,
                    options,
                    new V2OpenTelemetryBinding("tenant", "other-scope", "collector"));
                await using (foreign)
                {
                    await using var lease = await ((IDiagnosticsPersistenceStartupResource)foreign).AcquireAsync();
                    Assert.Empty((await foreign.QueryTracesAsync(new OpenTelemetryTraceFilter())).Items);
                }
            }
            finally { foreignConnection.Dispose(); }
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        static OpenTelemetryBatch CaptureBatch(string traceId)
        {
            var now = DateTimeOffset.UtcNow;
            var resource = new TelemetryResource("restart-resource", "restart-service", null, "dotnet", new Dictionary<string, string?>(), now, TelemetryResourceStatus.Active);
            var trace = new TelemetryTrace(traceId, "restart-root", "restart", now.AddSeconds(-1), now, TimeSpan.FromSeconds(1), SpanStatus.Ok, [resource.Id], [], 1);
            var span = new TelemetrySpan("restart-span", traceId, "restart-span", null, resource.Id, "restart", "server", trace.StartTime, trace.EndTime, SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []);
            return new OpenTelemetryBatch([resource], [trace], [span], [], [], []);
        }
    }
}
