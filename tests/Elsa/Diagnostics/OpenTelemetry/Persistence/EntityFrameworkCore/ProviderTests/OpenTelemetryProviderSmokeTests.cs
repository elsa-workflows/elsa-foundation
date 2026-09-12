using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Stores;
using Elsa.Diagnostics.Persistence.Draining;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class OpenTelemetryProviderModelTests
{
    [Theory]
    [InlineData("Sqlite", "Microsoft.EntityFrameworkCore.Sqlite", "OpenTelemetrySqliteDbContext", "TEXT")]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer", "OpenTelemetrySqlServerDbContext", "nvarchar(max)")]
    [InlineData("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL", "OpenTelemetryPostgreSqlDbContext", "text")]
    [InlineData("MySql", "MySql.EntityFrameworkCore", "OpenTelemetryMySqlDbContext", "longtext")]
    public async Task Provider_models_build_without_connecting(
        string provider,
        string expectedProvider,
        string expectedContextName,
        string expectedPayloadType)
    {
        await using var services = OpenTelemetryProviderTestHost.BuildProvider(
            provider,
            provider == "Sqlite"
                ? $"Data Source={Path.Combine(Path.GetTempPath(), "elsa-otel-model-" + Guid.NewGuid().ToString("N") + ".db")}"
                : provider switch
                {
                    "SqlServer" => "Server=localhost;Database=unused;Integrated Security=True;TrustServerCertificate=True",
                    "PostgreSql" => "Host=localhost;Database=unused",
                    "MySql" => "Server=localhost;Database=unused",
                    _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
                });

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenTelemetryDbContext>();

        Assert.Equal(expectedProvider, context.Database.ProviderName);
        Assert.Equal(expectedContextName, context.GetType().Name);
        var entityTypes = context.Model.GetEntityTypes().ToArray();
        Assert.NotEmpty(entityTypes);
        Assert.All(entityTypes, entity => Assert.NotNull(entity.FindPrimaryKey()));
        var payloadEntities = entityTypes
            .Where(entity => entity.FindProperty("PayloadJson") is not null)
            .ToArray();
        Assert.Equal(7, payloadEntities.Length);
        Assert.All(payloadEntities, entity => Assert.Equal(
            expectedPayloadType,
            entity.FindProperty("PayloadJson")!.GetColumnType(),
            ignoreCase: true));
        Assert.All(entityTypes, entity => Assert.Contains(
            "ScopeKey",
            entity.FindPrimaryKey()!.Properties.Select(property => property.Name)));
    }
}

[Collection(OpenTelemetryPostgreSqlFixture.CollectionName)]
public sealed class OpenTelemetryPostgreSqlSmokeTests(OpenTelemetryPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task All_signal_round_trip_restart_and_scope_isolation_on_postgresql() =>
        OpenTelemetryProviderSmoke.RunAsync(fixture, "PostgreSql");

    [SkippableFact]
    public Task Concurrent_trace_summary_upserts_are_cas_safe_on_postgresql() =>
        OpenTelemetryProviderSmoke.RunSummaryCasAsync(fixture, "PostgreSql");

    [SkippableFact]
    public Task Trace_retention_recomputes_summary_and_memberships_on_postgresql() =>
        OpenTelemetryProviderSmoke.RunRetentionAsync(fixture, "PostgreSql");
}

[Collection(OpenTelemetrySqlServerFixture.CollectionName)]
public sealed class OpenTelemetrySqlServerSmokeTests(OpenTelemetrySqlServerFixture fixture)
{
    [SkippableFact]
    public Task All_signal_round_trip_restart_and_scope_isolation_on_sql_server() =>
        OpenTelemetryProviderSmoke.RunAsync(fixture, "SqlServer");

    [SkippableFact]
    public Task Concurrent_trace_summary_upserts_are_cas_safe_on_sql_server() =>
        OpenTelemetryProviderSmoke.RunSummaryCasAsync(fixture, "SqlServer");

    [SkippableFact]
    public Task Trace_retention_recomputes_summary_and_memberships_on_sql_server() =>
        OpenTelemetryProviderSmoke.RunRetentionAsync(fixture, "SqlServer");
}

[Collection(OpenTelemetryMySqlFixture.CollectionName)]
public sealed class OpenTelemetryMySqlSmokeTests(OpenTelemetryMySqlFixture fixture)
{
    [SkippableFact]
    public Task All_signal_round_trip_restart_and_scope_isolation_on_mysql() =>
        OpenTelemetryProviderSmoke.RunAsync(fixture, "MySql");

    [SkippableFact]
    public Task Concurrent_trace_summary_upserts_are_cas_safe_on_mysql() =>
        OpenTelemetryProviderSmoke.RunSummaryCasAsync(fixture, "MySql");

    [SkippableFact]
    public Task Trace_retention_recomputes_summary_and_memberships_on_mysql() =>
        OpenTelemetryProviderSmoke.RunRetentionAsync(fixture, "MySql");
}

internal static class OpenTelemetryProviderSmoke
{
    public static async Task RunAsync(OpenTelemetryProviderFixture fixture, string provider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"{provider} is unavailable.");

        var scopeKey = "otel-smoke-" + Guid.NewGuid().ToString("N");
        var data = OpenTelemetryProviderTestHost.CreateBatch(provider);
        await using (var services = OpenTelemetryProviderTestHost.BuildProvider(provider, fixture.ConnectionString, scopeKey))
        {
            await OpenTelemetryProviderTestHost.EnsureCreatedAsync(services);
            var store = services.GetRequiredService<EfOpenTelemetryStore>();
            var drain = (IDiagnosticsPersistenceDrain)store;
            drain.Start();

            await store.WriteAsync(data.Batch);
            await OpenTelemetryProviderTestHost.WaitForDiagnosticsAsync(
                store,
                diagnostics => diagnostics.ResourceCount == 1 &&
                               diagnostics.TraceCount == 1 &&
                               diagnostics.SpanCount == 1 &&
                               diagnostics.MetricInstrumentCount == 1 &&
                               diagnostics.MetricPointCount == 1 &&
                               diagnostics.LogRecordCount == 1);

            var resources = await store.QueryResourcesAsync(new OpenTelemetryResourceFilter
            {
                ServiceName = data.Resource.ServiceName,
                Take = 10
            });
            Assert.Equal(data.Resource.Id, Assert.Single(resources.Items).Id);

            var traces = await store.QueryTracesAsync(new OpenTelemetryTraceFilter
            {
                WorkflowInstanceId = "workflow-provider-smoke",
                Take = 10
            });
            Assert.Equal(data.Trace.TraceId, Assert.Single(traces.Items).TraceId);

            var detail = await store.GetTraceAsync(data.Trace.TraceId.ToUpperInvariant());
            Assert.NotNull(detail);
            Assert.Equal(data.Trace.TraceId, detail!.Trace.TraceId);
            Assert.Equal(data.Span.SpanId, Assert.Single(detail.Spans).SpanId);
            Assert.Equal(data.Resource.Id, Assert.Single(detail.Resources).Id);
            Assert.Equal(data.Log.Id, Assert.Single(detail.Logs).Id);
            Assert.Equal("GET", detail.Spans.Single().Attributes["http.method"]);
            Assert.Equal("event-value", detail.Spans.Single().Events.Single().Attributes["event.key"]);
            Assert.Equal("link-value", detail.Spans.Single().Links.Single().Attributes["link.key"]);

            var metrics = await store.QueryMetricsAsync(new OpenTelemetryMetricFilter
            {
                ServiceName = data.Resource.ServiceName,
                InstrumentName = "request.duration",
                Take = 10
            });
            Assert.Equal(data.Instrument.Id, Assert.Single(metrics.Instruments).Id);
            Assert.Equal(data.Point.Id, Assert.Single(metrics.Points).Id);

            var logs = await store.QueryLogsAsync(new OpenTelemetryLogFilter
            {
                Search = "provider smoke log",
                Take = 10
            });
            Assert.Equal(data.Log.Id, Assert.Single(logs.Items).Id);

            await drain.StopAsync();
        }

        await using var restartedServices = OpenTelemetryProviderTestHost.BuildProvider(provider, fixture.ConnectionString, scopeKey);
        await OpenTelemetryProviderTestHost.EnsureCreatedAsync(restartedServices);
        var restartedStore = restartedServices.GetRequiredService<EfOpenTelemetryStore>();
        var restartedDrain = (IDiagnosticsPersistenceDrain)restartedStore;
        restartedDrain.Start();

        var afterRestart = await restartedStore.GetDiagnosticsAsync();
        Assert.Equal(1, afterRestart.ResourceCount);
        Assert.Equal(1, afterRestart.TraceCount);
        Assert.Equal(1, afterRestart.SpanCount);
        Assert.Equal(1, afterRestart.MetricPointCount);
        Assert.Equal(1, afterRestart.LogRecordCount);
        Assert.Equal(data.Trace.TraceId, Assert.Single((await restartedStore.QueryTracesAsync(new() { Take = 10 })).Items).TraceId);
        Assert.Equal(data.Log.Id, Assert.Single((await restartedStore.QueryLogsAsync(new() { Take = 10 })).Items).Id);
        await restartedDrain.StopAsync();

        await RunScopeIsolationAsync(fixture.ConnectionString, provider);
    }

    public static async Task RunSummaryCasAsync(OpenTelemetryProviderFixture fixture, string provider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"{provider} is unavailable.");

        var scopeKey = "otel-cas-" + Guid.NewGuid().ToString("N");
        await using var firstServices = OpenTelemetryProviderTestHost.BuildProvider(provider, fixture.ConnectionString, scopeKey);
        await OpenTelemetryProviderTestHost.EnsureCreatedAsync(firstServices);
        await using var secondServices = OpenTelemetryProviderTestHost.BuildProvider(provider, fixture.ConnectionString, scopeKey);
        var first = firstServices.GetRequiredService<EfOpenTelemetryStore>();
        var second = secondServices.GetRequiredService<EfOpenTelemetryStore>();
        var firstDrain = (IDiagnosticsPersistenceDrain)first;
        var secondDrain = (IDiagnosticsPersistenceDrain)second;
        firstDrain.Start();
        secondDrain.Start();

        var now = DateTimeOffset.UtcNow;
        var firstStart = now.AddSeconds(-20);
        var firstEnd = now.AddSeconds(-10);
        var secondStart = now.AddSeconds(-5);
        var secondEnd = now.AddSeconds(5);
        var firstWrite = new TelemetryTrace(
            "trace-cas", "root-cas", "first summary", firstStart, firstEnd, firstEnd - firstStart,
            SpanStatus.Ok, ["resource-cas-a"], ["workflow-cas-a"], 1);
        var secondWrite = firstWrite with
        {
            RootSpanId = "root-late-cas",
            Name = "second summary",
            StartTime = secondStart,
            EndTime = secondEnd,
            Duration = secondEnd - secondStart,
            Status = SpanStatus.Error,
            ResourceIds = ["resource-cas-b"],
            WorkflowInstanceIds = ["workflow-cas-b"],
            SpanCount = 2,
        };

        await Task.WhenAll(
            first.WriteAsync(new OpenTelemetryBatch([], [firstWrite], [], [], [], [])).AsTask(),
            second.WriteAsync(new OpenTelemetryBatch([], [secondWrite], [], [], [], [])).AsTask());

        var survivor = await OpenTelemetryProviderTestHost.WaitForTraceAsync(
            first,
            "TRACE-CAS",
            trace => trace.TraceId == firstWrite.TraceId &&
                     trace.RootSpanId == firstWrite.RootSpanId &&
                     trace.Name == firstWrite.Name &&
                     trace.StartTime == firstWrite.StartTime &&
                     trace.EndTime == secondWrite.EndTime &&
                     trace.Duration == secondWrite.EndTime - firstWrite.StartTime &&
                     trace.Status == SpanStatus.Error &&
                     trace.SpanCount == firstWrite.SpanCount + secondWrite.SpanCount &&
                     trace.ResourceIds.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(new[] { "resource-cas-a", "resource-cas-b" }.Order(StringComparer.OrdinalIgnoreCase)) &&
                     trace.WorkflowInstanceIds.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(new[] { "workflow-cas-a", "workflow-cas-b" }.Order(StringComparer.OrdinalIgnoreCase)));
        Assert.Equal(firstWrite.StartTime, survivor.StartTime);
        Assert.Equal(secondWrite.EndTime, survivor.EndTime);
        Assert.Equal(3, survivor.SpanCount);
        await firstDrain.StopAsync();
        await secondDrain.StopAsync();
    }

    public static async Task RunRetentionAsync(OpenTelemetryProviderFixture fixture, string provider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"{provider} is unavailable.");

        var scopeKey = "otel-retention-" + Guid.NewGuid().ToString("N");
        var options = new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 2,
            SpanCapacity = 10,
            MetricPointCapacity = 10,
            LogRecordCapacity = 10,
            ResourceCapacity = 10,
            MetricInstrumentCapacity = 10,
            MaxQuerySize = 20
        };
        await using var services = OpenTelemetryProviderTestHost.BuildProvider(provider, fixture.ConnectionString, scopeKey, options);
        await OpenTelemetryProviderTestHost.EnsureCreatedAsync(services);
        var store = services.GetRequiredService<EfOpenTelemetryStore>();
        var drain = (IDiagnosticsPersistenceDrain)store;
        drain.Start();

        var start = DateTimeOffset.UtcNow.AddMinutes(-1);
        var api = new TelemetryResource("retained-api", "orders-api", null, "dotnet", new Dictionary<string, string?>(), start, TelemetryResourceStatus.Active);
        var worker = api with { Id = "retained-worker", ServiceName = "orders-worker" };
        var other = api with { Id = "retained-other", ServiceName = "other" };
        var first = new TelemetryTrace("retained-shared", "root-old", "old", start, start.AddSeconds(1), TimeSpan.FromSeconds(1), SpanStatus.Error, [api.Id], ["workflow-old"], 1);
        var unrelated = new TelemetryTrace("retained-other", "root-other", "other", start.AddSeconds(2), start.AddSeconds(3), TimeSpan.FromSeconds(1), SpanStatus.Ok, [other.Id], [], 4);
        var latest = new TelemetryTrace("retained-shared", null, "new", start.AddSeconds(4), start.AddSeconds(5), TimeSpan.FromSeconds(1), SpanStatus.Ok, [api.Id, worker.Id], ["workflow-new"], 2);

        await store.WriteAsync(new([api, worker, other], [first], [], [], [], []));
        await store.WriteAsync(new([], [unrelated], [], [], [], []));
        await store.WriteAsync(new([], [latest], [], [], [], []));

        var retained = (await store.GetTraceAsync("RETAINED-SHARED"))!.Trace;
        Assert.Equal(latest.StartTime, retained.StartTime);
        Assert.Equal(latest.EndTime, retained.EndTime);
        Assert.Equal(latest.SpanCount, retained.SpanCount);
        Assert.Equal(SpanStatus.Ok, retained.Status);
        Assert.Equal(["workflow-new"], retained.WorkflowInstanceIds);
        Assert.Equal([latest.TraceId], (await store.QueryTracesAsync(new() { ServiceName = "ORDERS-API", Take = 10 })).Items.Select(x => x.TraceId));
        Assert.Equal([latest.TraceId], (await store.QueryTracesAsync(new() { ServiceName = "ORDERS-WORKER", Take = 10 })).Items.Select(x => x.TraceId));
        await drain.StopAsync();
    }

    private static async Task RunScopeIsolationAsync(string connectionString, string provider)
    {
        var firstScope = "otel-scope-a-" + Guid.NewGuid().ToString("N");
        var secondScope = "otel-scope-b-" + Guid.NewGuid().ToString("N");
        await using var firstServices = OpenTelemetryProviderTestHost.BuildProvider(provider, connectionString, firstScope);
        await using var secondServices = OpenTelemetryProviderTestHost.BuildProvider(provider, connectionString, secondScope);
        var first = firstServices.GetRequiredService<EfOpenTelemetryStore>();
        var second = secondServices.GetRequiredService<EfOpenTelemetryStore>();
        await OpenTelemetryProviderTestHost.EnsureCreatedAsync(firstServices);
        var firstDrain = (IDiagnosticsPersistenceDrain)first;
        var secondDrain = (IDiagnosticsPersistenceDrain)second;
        firstDrain.Start();
        secondDrain.Start();

        var firstResource = new TelemetryResource(
            "same-resource", "service-a", null, "dotnet", new Dictionary<string, string?>(), DateTimeOffset.UtcNow, TelemetryResourceStatus.Active);
        var secondResource = firstResource with { ServiceName = "service-b" };
        await first.WriteAsync(new OpenTelemetryBatch([firstResource], [], [], [], [], [])).AsTask();
        await second.WriteAsync(new OpenTelemetryBatch([secondResource], [], [], [], [], [])).AsTask();
        await OpenTelemetryProviderTestHost.WaitForDiagnosticsAsync(first, diagnostics => diagnostics.ResourceCount == 1);
        await OpenTelemetryProviderTestHost.WaitForDiagnosticsAsync(second, diagnostics => diagnostics.ResourceCount == 1);

        Assert.Equal("service-a", Assert.Single((await first.QueryResourcesAsync(new() { Take = 10 })).Items).ServiceName);
        Assert.Equal("service-b", Assert.Single((await second.QueryResourcesAsync(new() { Take = 10 })).Items).ServiceName);
        Assert.Empty((await first.QueryResourcesAsync(new() { ServiceName = "service-b", Take = 10 })).Items);
        Assert.Empty((await second.QueryResourcesAsync(new() { ServiceName = "service-a", Take = 10 })).Items);
        await firstDrain.StopAsync();
        await secondDrain.StopAsync();
    }
}

internal static class OpenTelemetryProviderTestHost
{
    public static ServiceProvider BuildProvider(
        string provider,
        string connectionString,
        string? scopeKey = null,
        OpenTelemetryDiagnosticsOptions? diagnostics = null)
    {
        var services = new ServiceCollection();
        new OpenTelemetryFeature().ConfigureServices(services);
        if (diagnostics is not null)
            services.Configure<OpenTelemetryDiagnosticsOptions>(configured => Copy(diagnostics, configured));
        var options = new OpenTelemetryEntityFrameworkCoreOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            TenantId = "provider-test-tenant",
            ScopeId = scopeKey ?? "provider-test-scope",
            SourceId = "provider-test-source"
        };

        services.AddOpenTelemetryEntityFrameworkCore(options);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static void Copy(OpenTelemetryDiagnosticsOptions source, OpenTelemetryDiagnosticsOptions destination)
    {
        destination.TraceCapacity = source.TraceCapacity;
        destination.SpanCapacity = source.SpanCapacity;
        destination.MetricPointCapacity = source.MetricPointCapacity;
        destination.LogRecordCapacity = source.LogRecordCapacity;
        destination.ResourceCapacity = source.ResourceCapacity;
        destination.MetricInstrumentCapacity = source.MetricInstrumentCapacity;
        destination.SubscriberChannelCapacity = source.SubscriberChannelCapacity;
        destination.MaxQuerySize = source.MaxQuerySize;
        destination.ShutdownDrainTimeout = source.ShutdownDrainTimeout;
    }

    public static async Task EnsureCreatedAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OpenTelemetryDbContext>().Database.EnsureCreatedAsync();
    }

    public static async Task WaitForDiagnosticsAsync(
        IOpenTelemetryStore store,
        Func<OpenTelemetryStorageDiagnostics, bool> predicate,
        int timeoutMilliseconds = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(await store.GetDiagnosticsAsync()))
                return;
            await Task.Delay(25);
        }

        var final = await store.GetDiagnosticsAsync();
        Assert.True(predicate(final),
            $"Durable diagnostics did not reach the expected state: resources={final.ResourceCount}, traces={final.TraceCount}, spans={final.SpanCount}, instruments={final.MetricInstrumentCount}, points={final.MetricPointCount}, logs={final.LogRecordCount}.");
    }

    public static async Task<TelemetryTrace> WaitForTraceAsync(
        IOpenTelemetryStore store,
        string traceId,
        Func<TelemetryTrace, bool> predicate,
        int timeoutMilliseconds = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        TelemetryTrace? last = null;
        while (DateTime.UtcNow < deadline)
        {
            var result = await store.QueryTracesAsync(new OpenTelemetryTraceFilter { TraceId = traceId, Take = 10 });
            last = result.Items.SingleOrDefault();
            if (last is not null && predicate(last))
                return last;
            await Task.Delay(25);
        }

        Assert.NotNull(last);
        Assert.True(predicate(last!), $"Durable trace summary did not reach the expected merged state: {last}");
        return last!;
    }

    public static (OpenTelemetryBatch Batch, TelemetryResource Resource, TelemetryTrace Trace, TelemetrySpan Span, MetricInstrument Instrument, MetricPoint Point, OtlpLogRecord Log) CreateBatch(string provider)
    {
        var now = DateTimeOffset.UtcNow;
        var resource = new TelemetryResource(
            "resource-" + provider.ToLowerInvariant(),
            "provider-" + provider.ToLowerInvariant(),
            "instance-1",
            "dotnet",
            new Dictionary<string, string?> { ["deployment.environment"] = "provider-test" },
            now,
            TelemetryResourceStatus.Active);
        var trace = new TelemetryTrace(
            "trace-" + provider.ToLowerInvariant(),
            "root-span",
            "provider operation",
            now,
            now.AddSeconds(1),
            TimeSpan.FromSeconds(1),
            SpanStatus.Ok,
            [resource.Id],
            ["workflow-provider-smoke"],
            1);
        var span = new TelemetrySpan(
            "span-record-" + provider.ToLowerInvariant(),
            trace.TraceId,
            "span-1",
            null,
            resource.Id,
            "GET /provider",
            "server",
            now,
            now.AddMilliseconds(10),
            SpanStatus.Ok,
            null,
            new Dictionary<string, string?> { ["http.method"] = "GET" },
            [new TelemetrySpanEvent("event", now.AddMilliseconds(1), new Dictionary<string, string?> { ["event.key"] = "event-value" })],
            [new TelemetrySpanLink("linked-trace", "linked-span", new Dictionary<string, string?> { ["link.key"] = "link-value" })]);
        var instrument = new MetricInstrument(
            "instrument-" + provider.ToLowerInvariant(),
            resource.Id,
            "request.duration",
            "ms",
            "provider test duration",
            MetricKind.Histogram,
            new Dictionary<string, string?> { ["instrument.key"] = "instrument-value" });
        var point = new MetricPoint(
            "point-" + provider.ToLowerInvariant(),
            instrument.Id,
            instrument.Name,
            resource.Id,
            now,
            42,
            null,
            1,
            new Dictionary<string, string?> { ["point.key"] = "point-value" },
            trace.TraceId,
            span.SpanId);
        var log = new OtlpLogRecord(
            "log-" + provider.ToLowerInvariant(),
            resource.Id,
            now,
            "Warning",
            13,
            "provider smoke log",
            trace.TraceId,
            span.SpanId,
            new Dictionary<string, string?> { ["log.key"] = "log-value" });
        return (new([resource], [trace], [span], [instrument], [point], [log]), resource, trace, span, instrument, point, log);
    }
}
