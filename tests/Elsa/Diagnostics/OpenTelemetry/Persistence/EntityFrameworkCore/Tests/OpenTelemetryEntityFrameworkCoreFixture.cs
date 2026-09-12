using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Tests;

internal sealed class OpenTelemetryEntityFrameworkCoreFixture : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "elsa-otel-ef-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider? provider;

    public string DatabasePath => Path.Combine(directory, "opentelemetry.db");
    public IOpenTelemetryStore Store => provider!.GetRequiredService<IOpenTelemetryStore>();
    public EfOpenTelemetryStore EfStore => provider!.GetRequiredService<EfOpenTelemetryStore>();
    public IOpenTelemetrySourceRegistry SourceRegistry => provider!.GetRequiredService<IOpenTelemetrySourceRegistry>();

    public async Task InitializeAsync(OpenTelemetryDiagnosticsOptions? diagnostics = null)
    {
        Directory.CreateDirectory(directory);
        provider = BuildProvider(DatabasePath, diagnostics);
        await EnsureCreatedAsync(provider);
        EfStore.Start();
    }

    public static ServiceProvider BuildProvider(string path, OpenTelemetryDiagnosticsOptions? diagnostics = null)
    {
        var services = new ServiceCollection();
        services.AddOptions<OpenTelemetryDiagnosticsOptions>();
        if (diagnostics is not null)
            services.Configure<OpenTelemetryDiagnosticsOptions>(options => Copy(diagnostics, options));

        services.AddOpenTelemetryEntityFrameworkCore(new OpenTelemetryEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={path}"
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public static async Task EnsureCreatedAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OpenTelemetryDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task WithDbAsync(Func<OpenTelemetryDbContext, Task> action)
    {
        await using var scope = provider!.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<OpenTelemetryDbContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<OpenTelemetryDbContext, Task<T>> action)
    {
        await using var scope = provider!.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<OpenTelemetryDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        if (provider is not null)
        {
            try
            {
                await EfStore.StopAsync();
            }
            finally
            {
                await provider.DisposeAsync();
            }
        }

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static void Copy(OpenTelemetryDiagnosticsOptions from, OpenTelemetryDiagnosticsOptions to)
    {
        to.TraceCapacity = from.TraceCapacity;
        to.SpanCapacity = from.SpanCapacity;
        to.MetricPointCapacity = from.MetricPointCapacity;
        to.LogRecordCapacity = from.LogRecordCapacity;
        to.ResourceCapacity = from.ResourceCapacity;
        to.MetricInstrumentCapacity = from.MetricInstrumentCapacity;
        to.SubscriberChannelCapacity = from.SubscriberChannelCapacity;
        to.MaxQuerySize = from.MaxQuerySize;
        to.ShutdownDrainTimeout = from.ShutdownDrainTimeout;
    }
}

internal static class TelemetryTestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    public static TelemetryResource Resource(string id, string serviceName, DateTimeOffset? lastSeen = null,
        TelemetryResourceStatus status = TelemetryResourceStatus.Active) =>
        new(id, serviceName, id + "-instance", "dotnet",
            new Dictionary<string, string?> { ["deployment.environment"] = "test" }, lastSeen ?? Now, status);

    public static TelemetryTrace Trace(string traceId, string resourceId, DateTimeOffset? start = null,
        SpanStatus status = SpanStatus.Ok, int spanCount = 2, params string[] workflowInstanceIds)
    {
        var beginning = start ?? Now;
        return new(traceId, traceId + "-root", "checkout", beginning, beginning.AddMilliseconds(25),
            TimeSpan.FromMilliseconds(25), status, [resourceId], workflowInstanceIds, spanCount);
    }

    public static TelemetrySpan Span(string id, string traceId, string spanId, string resourceId,
        DateTimeOffset? start = null)
    {
        var beginning = start ?? Now;
        return new(id, traceId, spanId, null, resourceId, "checkout", "server", beginning,
            beginning.AddMilliseconds(10), SpanStatus.Ok, null,
            new Dictionary<string, string?> { ["http.method"] = "GET" },
            [new TelemetrySpanEvent("event-a", beginning.AddMilliseconds(1),
                new Dictionary<string, string?> { ["event.attr"] = "value" })],
            [new TelemetrySpanLink("linked-trace", "linked-span",
                new Dictionary<string, string?> { ["link.attr"] = "value" })]);
    }

    public static MetricInstrument Instrument(string id, string resourceId, string name) =>
        new(id, resourceId, name, "ms", "duration", MetricKind.Gauge,
            new Dictionary<string, string?> { ["instrument.attr"] = "value" });

    public static MetricPoint Point(string id, string instrumentId, string resourceId,
        DateTimeOffset? timestamp = null, string? traceId = null, string? spanId = null) =>
        new(id, instrumentId, instrumentId, resourceId, timestamp ?? Now, 42, null, null,
            new Dictionary<string, string?> { ["point.attr"] = "value" }, traceId, spanId);

    public static OtlpLogRecord Log(string id, string resourceId, string traceId, string body = "message",
        string severity = "Information") =>
        new(id, resourceId, Now, severity, 9, body, traceId, traceId + "-span",
            new Dictionary<string, string?> { ["log.attr"] = "value" });

    public static OpenTelemetryBatch Batch(string traceId)
    {
        var resource = Resource(traceId + "-resource", "orders");
        var trace = Trace(traceId, resource.Id, spanCount: 1);
        var span = Span(traceId + "-span-record", trace.TraceId, traceId + "-span", resource.Id);
        var instrument = Instrument(traceId + "-instrument", resource.Id, "requests");
        return new([resource], [trace], [span], [instrument],
            [Point(traceId + "-point", instrument.Id, resource.Id, traceId: trace.TraceId, spanId: span.SpanId)],
            [Log(traceId + "-log", resource.Id, trace.TraceId, "captured")]);
    }
}
