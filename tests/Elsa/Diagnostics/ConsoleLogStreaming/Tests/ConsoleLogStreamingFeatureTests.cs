using ConsoleLogStreaming.AspNetCore;
using ConsoleLogStreaming.Core.Hosting;
using ConsoleLogStreaming.Core.Options;
using CShells.AspNetCore.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.ConsoleLogStreaming.Tests;

public sealed class ConsoleLogStreamingFeatureTests
{
    [Fact]
    public void RegistersResolvableDiagnosticsServices()
    {
        var services = new ServiceCollection();

        new ConsoleLogStreamingFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ConsoleLogStreamingHostRegistration));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<ConsoleLogOptions>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<ConsoleLogStreamingAspNetCoreOptions>));
    }

    [Fact]
    public void AppliesConfiguredOptions()
    {
        var services = new ServiceCollection();

        new ConsoleLogStreamingFeature
        {
            ServiceName = "custom-service",
            SourceDisplayName = "Custom Source",
            RecentCapacity = 123,
            MaxRecentQuerySize = 45,
            PreserveAnsi = false,
            EndpointPrefix = "/custom/console"
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var hostOptions = provider.GetRequiredService<IOptions<ConsoleLogOptions>>().Value;
        var aspNetCoreOptions = provider.GetRequiredService<IOptions<ConsoleLogStreamingAspNetCoreOptions>>().Value;

        Assert.Equal("custom-service", hostOptions.ServiceName);
        Assert.Equal("Custom Source", hostOptions.SourceDisplayName);
        Assert.Equal(123, hostOptions.RecentCapacity);
        Assert.Equal(45, hostOptions.MaxRecentQuerySize);
        Assert.False(hostOptions.PreserveAnsi);
        Assert.Equal("/custom/console/recent", aspNetCoreOptions.RecentPath);
        Assert.Equal("/custom/console/sources", aspNetCoreOptions.SourcesPath);
        Assert.Equal("/custom/console/hub", aspNetCoreOptions.HubPath);
    }

    [Fact]
    public void AppliesExplicitEndpointPaths()
    {
        var services = new ServiceCollection();

        new ConsoleLogStreamingFeature
        {
            RecentPath = "diagnostics/recent",
            SourcesPath = "/diagnostics/sources",
            HubPath = "diagnostics/hub"
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ConsoleLogStreamingAspNetCoreOptions>>().Value;

        Assert.Equal("/diagnostics/recent", options.RecentPath);
        Assert.Equal("/diagnostics/sources", options.SourcesPath);
        Assert.Equal("/diagnostics/hub", options.HubPath);
    }

    [Fact]
    public void ExposesWebFeatureEndpointMapping()
    {
        var feature = new ConsoleLogStreamingFeature();

        Assert.IsAssignableFrom<IWebShellFeature>(feature);
    }
}
