using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
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
        Assert.Same(store, provider.GetRequiredService<IStructuredLogLiveFeed>());
        Assert.Same(store, provider.GetRequiredService<IStructuredLogSink>());
        Assert.NotNull(provider.GetRequiredService<IStructuredLogSourceProvider>());
        Assert.NotNull(provider.GetRequiredService<StructuredLogEntrySerializer>());
        Assert.NotNull(provider.GetRequiredService<StructuredLogSseFormatter>());
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
}
