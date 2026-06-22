using ConsoleLogStreaming.AspNetCore;
using ConsoleLogStreaming.Core;
using ConsoleLogStreaming.Core.Capture;
using ConsoleLogStreaming.Core.Hosting;
using ConsoleLogStreaming.Core.Options;
using CShells.AspNetCore.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text;
using Xunit;

namespace Elsa.Diagnostics.ConsoleLogStreaming.Tests;

public sealed class ConsoleLogStreamingFeatureTests : IDisposable
{
    public ConsoleLogStreamingFeatureTests()
    {
        ConsoleLogStreamingHost.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
        ConsoleStreamHook.Uninstall();
        ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
    }

    public void Dispose()
    {
        ConsoleLogStreamingHost.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
        ConsoleStreamHook.Uninstall();
        ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
    }

    [Fact]
    public void RegistersResolvableDiagnosticsServices()
    {
        var services = new ServiceCollection();

        new ConsoleLogStreamingFeature(() => { }).ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConsoleLogCapture));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<ConsoleLogOptions>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<ConsoleLogStreamingAspNetCoreOptions>));
    }

    [Fact]
    public void RegistersHostLevelServicesOnlyOnce()
    {
        var services = new ServiceCollection();

        new ConsoleLogStreamingFeature(() => { })
        {
            ServiceName = "first-service",
            RecentCapacity = 10,
            EndpointPrefix = "/first"
        }.ConfigureServices(services);
        new ConsoleLogStreamingFeature(() => { })
        {
            ServiceName = "second-service",
            RecentCapacity = 20,
            EndpointPrefix = "/second"
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var hostOptions = provider.GetRequiredService<IOptions<ConsoleLogOptions>>().Value;
        var aspNetCoreOptions = provider.GetRequiredService<IOptions<ConsoleLogStreamingAspNetCoreOptions>>().Value;

        Assert.Equal("first-service", hostOptions.ServiceName);
        Assert.Equal(10, hostOptions.RecentCapacity);
        Assert.Equal("/first/recent", aspNetCoreOptions.RecentPath);
    }

    [Fact]
    public void ConfigureServicesDoesNotInstallRealConsoleStreamHookWhenHookDelegateIsInjected()
    {
        var services = new ServiceCollection();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        new ConsoleLogStreamingFeature(() => { }).ConfigureServices(services);

        Assert.Same(originalOut, Console.Out);
        Assert.Same(originalError, Console.Error);
    }

    [Fact]
    public void AppliesConfiguredOptions()
    {
        var services = new ServiceCollection();

        new ConsoleLogStreamingFeature(() => { })
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
    public void ClampsConfiguredHostLimits()
    {
        var services = new ServiceCollection();

        new ConsoleLogStreamingFeature(() => { })
        {
            RecentCapacity = 0,
            MaxRecentQuerySize = -1
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var hostOptions = provider.GetRequiredService<IOptions<ConsoleLogOptions>>().Value;

        Assert.Equal(1, hostOptions.RecentCapacity);
        Assert.Equal(1, hostOptions.MaxRecentQuerySize);
    }

    [Fact]
    public void AppliesExplicitEndpointPaths()
    {
        var services = new ServiceCollection();

        new ConsoleLogStreamingFeature(() => { })
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
        var feature = new ConsoleLogStreamingFeature(() => { });

        Assert.IsAssignableFrom<IWebShellFeature>(feature);
    }

    [Fact]
    public void InstallsConsoleStreamHookOnlyWhenFeatureIsEnabled()
    {
        ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
        var installCount = 0;
        var disabledConfiguration = BuildConfiguration("DiagnosticsStructuredLogs");
        var enabledConfiguration = BuildConfiguration(ConsoleLogStreamingFeature.FeatureName);

        try
        {
            ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled(disabledConfiguration, () => installCount++);
            ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled(enabledConfiguration, () => installCount++);
            ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled(enabledConfiguration, () => installCount++);

            Assert.Equal(1, installCount);
        }
        finally
        {
            ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
        }
    }

    [Fact]
    public void DoesNotEnableConsoleStreamHookWhenFeatureIsMissingFromConfiguration()
    {
        var configuration = BuildConfiguration();

        var enabled = ConsoleLogStreamingFeature.IsFeatureEnabled(configuration);

        Assert.False(enabled);
    }

    [Fact]
    public void DetectsEnabledFeatureFromEmptyObjectJsonShape()
    {
        var configuration = BuildJsonConfiguration("""
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

        var enabled = ConsoleLogStreamingFeature.IsFeatureEnabled(configuration);

        Assert.True(enabled);
    }

    [Fact]
    public void DoesNotEnableConsoleStreamHookWhenFeatureIsBooleanFalse()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": {
                "Shells": {
                  "default": {
                    "Features": {
                      "DiagnosticsConsoleLogStreaming": false
                    }
                  }
                }
              }
            }
            """);

        var enabled = ConsoleLogStreamingFeature.IsFeatureEnabled(configuration);

        Assert.False(enabled);
    }

    [Fact]
    public void DetectsEnabledFeatureCaseInsensitively()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": {
                "Shells": {
                  "default": {
                    "Features": {
                      "diagnosticsconsolelogstreaming": {}
                    }
                  }
                }
              }
            }
            """);

        var enabled = ConsoleLogStreamingFeature.IsFeatureEnabled(configuration);

        Assert.True(enabled);
    }

    [Fact]
    public void DetectsEnabledFeatureFromArrayJsonShape()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": {
                "Shells": {
                  "default": {
                    "Features": [
                      "DiagnosticsConsoleLogStreaming"
                    ]
                  }
                }
              }
            }
            """);

        var enabled = ConsoleLogStreamingFeature.IsFeatureEnabled(configuration);

        Assert.True(enabled);
    }

    [Fact]
    public void DetectsEnabledFeatureFromArrayObjectJsonShape()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": {
                "Shells": {
                  "default": {
                    "Features": [
                      {
                        "Name": "DiagnosticsConsoleLogStreaming",
                        "EndpointPrefix": "/diagnostics/console"
                      }
                    ]
                  }
                }
              }
            }
            """);

        var enabled = ConsoleLogStreamingFeature.IsFeatureEnabled(configuration);

        Assert.True(enabled);
    }

    [Fact]
    public void DoesNotEnableConsoleStreamHookWhenArrayObjectFeatureIsDisabled()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": {
                "Shells": {
                  "default": {
                    "Features": [
                      {
                        "Name": "DiagnosticsConsoleLogStreaming",
                        "Enabled": false
                      }
                    ]
                  }
                }
              }
            }
            """);

        var enabled = ConsoleLogStreamingFeature.IsFeatureEnabled(configuration);

        Assert.False(enabled);
    }

    [Fact]
    public void InstallsConsoleStreamHookFromContentRootBeforeBuilderCreation()
    {
        ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        var otherDirectory = Directory.CreateTempSubdirectory();

        try
        {
            File.WriteAllText(Path.Combine(contentRoot.FullName, "shells.json"), """
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
            Directory.SetCurrentDirectory(otherDirectory.FullName);

            ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled(["--contentRoot", contentRoot.FullName], () => installCount++);

            Assert.Equal(1, installCount);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
            contentRoot.Delete(recursive: true);
            otherDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InstallsConsoleStreamHookWhenFeatureActivatesAtRuntime()
    {
        ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
        var installCount = 0;
        var services = new ServiceCollection();

        try
        {
            new ConsoleLogStreamingFeature(() => installCount++).ConfigureServices(services);
            new ConsoleLogStreamingFeature(() => installCount++).ConfigureServices(new ServiceCollection());

            Assert.Equal(1, installCount);
        }
        finally
        {
            ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
        }
    }

    private static IConfiguration BuildConfiguration(params string[] featureNames)
    {
        var values = featureNames
            .Select(featureName => new KeyValuePair<string, string?>($"CShells:Shells:default:Features:{featureName}:Enabled", "true"));

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IConfiguration BuildJsonConfiguration(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }
}
