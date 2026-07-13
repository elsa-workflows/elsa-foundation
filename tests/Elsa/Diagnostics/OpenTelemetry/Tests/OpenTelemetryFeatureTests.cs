using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Endpoints;
using Elsa.Diagnostics.Persistence.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class OpenTelemetryFeatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
    [Fact]
    public void RegistersResolvableDiagnosticsServices()
    {
        var services = new ServiceCollection();

        new OpenTelemetryFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOpenTelemetryStore>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetryLiveFeed>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetryIngestor>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetryProvider>());
        Assert.NotNull(provider.GetRequiredService<ICollectorConfigurationProvider>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetrySourceRegistry>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetryRedactor>());
        Assert.NotNull(provider.GetRequiredService<OpenTelemetryStreamItemSerializer>());
        Assert.NotNull(provider.GetRequiredService<OpenTelemetrySseFormatter>());
        Assert.NotNull(provider.GetRequiredService<OpenTelemetrySseStreamWriter>());
        Assert.NotNull(provider.GetRequiredService<OpenTelemetryTraceFilterBinder>());
        Assert.NotNull(provider.GetRequiredService<IOptions<OpenTelemetryDiagnosticsOptions>>().Value);
    }

    [Fact]
    public void AppliesConfiguredOptions()
    {
        var services = new ServiceCollection();

        new OpenTelemetryFeature
        {
            TraceCapacity = 123,
            ApiKey = "secret",
            AllowUnauthenticatedLoopback = false
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenTelemetryDiagnosticsOptions>>().Value;

        Assert.Equal(123, options.TraceCapacity);
        Assert.Equal("secret", options.ApiKey);
        Assert.False(options.AllowUnauthenticatedLoopback);
    }

    [Fact]
    public async Task Production_composition_counts_each_subscriber_drop_summary_once()
    {
        var services = new ServiceCollection();
        new OpenTelemetryFeature { SubscriberChannelCapacity = 1 }.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var feed = provider.GetRequiredService<IOpenTelemetryLiveFeed>();
        var counters = provider.GetRequiredService<DiagnosticsPersistenceCounters>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var subscriber = feed.SubscribeAsync(new OpenTelemetryTraceFilter(), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        var traces = Enumerable.Range(1, 3)
            .Select(index => new TelemetryTrace(
                $"trace-{index}", "root", "name", Now, Now.AddMilliseconds(5),
                TimeSpan.FromMilliseconds(5), SpanStatus.Ok, ["resource-1"], [], 1))
            .ToArray();

        await feed.PublishAsync(new OpenTelemetryBatch([], traces, [], [], [], []), cancellation.Token);
        Assert.True(await subscriber.MoveNextAsync());
        Assert.True(await subscriber.MoveNextAsync());

        var signal = Assert.IsType<OpenTelemetryDroppedItemSummary>(subscriber.Current.DroppedItems);
        Assert.Equal(signal.Count,
            counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.SubscriberDelivery]);
    }
}
