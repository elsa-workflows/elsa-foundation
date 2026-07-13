using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
using Elsa.Diagnostics.Persistence.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogsFeatureTests
{
    [Fact]
    public void RegistersResolvableDiagnosticsServices()
    {
        var services = new ServiceCollection();

        new StructuredLogsFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<IStructuredLogStore>();
        Assert.NotNull(store);
        Assert.NotNull(provider.GetRequiredService<IStructuredLogLiveFeed>());
        Assert.NotNull(provider.GetRequiredService<IStructuredLogLivePublisher>());
        Assert.NotNull(provider.GetRequiredService<IStructuredLogSink>());
        // The live feed and its publisher are the same instance.
        Assert.Same(
            provider.GetRequiredService<IStructuredLogLiveFeed>(),
            provider.GetRequiredService<IStructuredLogLivePublisher>());
        Assert.NotNull(provider.GetRequiredService<IStructuredLogSourceProvider>());
        Assert.NotNull(provider.GetRequiredService<StructuredLogEntrySerializer>());
        Assert.NotNull(provider.GetRequiredService<StructuredLogSseFormatter>());
        Assert.NotNull(provider.GetRequiredService<StructuredLogSseStreamWriter>());
        Assert.NotNull(provider.GetRequiredService<StructuredLogFilterBinder>());
        Assert.NotNull(provider.GetRequiredService<IOptions<StructuredLogsOptions>>().Value);
        Assert.Contains(provider.GetServices<ILoggerProvider>(), p => p.GetType().Name == "StructuredLogCaptureProvider");
    }

    [Fact]
    public void AppliesConfiguredOptions()
    {
        var services = new ServiceCollection();

        new StructuredLogsFeature
        {
            MinimumLevel = "Warning",
            BufferCapacity = 123
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<StructuredLogsOptions>>().Value;

        Assert.Equal(LogLevel.Warning, options.MinimumLevel);
        Assert.Equal(123, options.BufferCapacity);
    }

    [Fact]
    public async Task Production_composition_counts_each_subscriber_drop_once()
    {
        var services = new ServiceCollection();
        new StructuredLogsFeature().ConfigureServices(services);
        services.Configure<StructuredLogsOptions>(options => options.SubscriberQueueCapacity = 1);
        using var provider = services.BuildServiceProvider();
        var feed = provider.GetRequiredService<IStructuredLogLiveFeed>();
        var publisher = provider.GetRequiredService<IStructuredLogLivePublisher>();
        var counters = provider.GetRequiredService<DiagnosticsPersistenceCounters>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var subscriber = feed.Subscribe(StructuredLogFilter.None, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        publisher.Publish(TestEntries.Create(sequence: 1));
        publisher.Publish(TestEntries.Create(sequence: 2));
        publisher.Publish(TestEntries.Create(sequence: 3));
        Assert.True(await subscriber.MoveNextAsync());
        Assert.True(await subscriber.MoveNextAsync());

        var signal = Assert.IsType<Core.Models.DroppedEntriesSignal>(subscriber.Current.Dropped);
        Assert.Equal(signal.DroppedCount,
            counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.SubscriberDelivery]);
    }
}
