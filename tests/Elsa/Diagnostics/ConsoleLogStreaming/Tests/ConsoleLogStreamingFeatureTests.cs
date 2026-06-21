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

    [Fact]
    public void InstallsConsoleStreamHookOnlyWhenFeatureIsEnabled()
    {
        ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
        var installCount = 0;
        var disabledPath = WriteShellsJson("""
            {
              "CShells": {
                "Shells": {
                  "default": {
                    "Features": {
                      "DiagnosticsStructuredLogs": {}
                    }
                  }
                }
              }
            }
            """);
        var enabledPath = WriteShellsJson("""
            {
              "CShells": {
                "Shells": {
                  "default": {
                    "Features": {
                      "DiagnosticsConsoleLogStreaming": {}
                    }
                  }
                }
              }
            }
            """);

        try
        {
            ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled(disabledPath, () => installCount++);
            ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled(enabledPath, () => installCount++);
            ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled(enabledPath, () => installCount++);

            Assert.Equal(1, installCount);
        }
        finally
        {
            ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
            File.Delete(disabledPath);
            File.Delete(enabledPath);
        }
    }

    [Fact]
    public void DoesNotEnableConsoleStreamHookWhenShellsJsonIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        var enabled = ConsoleLogStreamingFeature.IsFeatureEnabled(missingPath);

        Assert.False(enabled);
    }

    private static string WriteShellsJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
