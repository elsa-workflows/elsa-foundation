using ConsoleLogStreaming.AspNetCore;
using ConsoleLogStreaming.Core;
using ConsoleLogStreaming.Core.Capture;
using ConsoleLogStreaming.Core.Options;
using CShells.AspNetCore.Features;
using CShells.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    public void IsAShellFeatureThatRegistersNothingPerShell()
    {
        // Console log streaming is composed entirely at the application root (see Program.cs). The shell feature is
        // only the enable/discover surface, so it must NOT be a web feature and must contribute nothing to the shell
        // container — a runtime enable then has no half-applied effect.
        var feature = new ConsoleLogStreamingFeature();
        var services = new ServiceCollection();

        feature.ConfigureServices(services);

        Assert.IsAssignableFrom<IShellFeature>(feature);
        Assert.IsNotAssignableFrom<IWebShellFeature>(feature);
        Assert.Empty(services);
    }

    [Fact]
    public void ConfigureHostAppliesDiagnosticsDefaults()
    {
        var options = new ConsoleLogOptions();

        ConsoleLogStreamingFeature.ConfigureHost(options);

        Assert.Equal("elsa-server", options.ServiceName);
        Assert.Equal("Elsa.Server", options.SourceDisplayName);
        Assert.Equal(2_000, options.RecentCapacity);
        Assert.Equal(2_000, options.MaxRecentQuerySize);
        Assert.True(options.PreserveAnsi);
    }

    [Fact]
    public void ConfigureEndpointsAppliesDefaultRoutes()
    {
        var options = new ConsoleLogStreamingAspNetCoreOptions();

        ConsoleLogStreamingFeature.ConfigureEndpoints(options);

        Assert.Equal(ConsoleLogStreamingFeature.DefaultRecentPath, options.RecentPath);
        Assert.Equal(ConsoleLogStreamingFeature.DefaultSourcesPath, options.SourcesPath);
        Assert.Equal(ConsoleLogStreamingFeature.DefaultHubPath, options.HubPath);
    }

    [Fact]
    public void DefaultRoutesShareTheDiagnosticsEndpointPrefix()
    {
        // The Studio ConsoleStream client targets these fixed paths; pin them so a refactor can't silently move the
        // host-mapped routes out from under the client.
        Assert.Equal("/_elsa/server/diagnostics/console-logs/recent", ConsoleLogStreamingFeature.DefaultRecentPath);
        Assert.Equal("/_elsa/server/diagnostics/console-logs/sources", ConsoleLogStreamingFeature.DefaultSourcesPath);
        Assert.Equal("/_elsa/server/diagnostics/console-logs/hub", ConsoleLogStreamingFeature.DefaultHubPath);
    }

    [Fact]
    public void IsFeatureEnabledReflectsFeatureConfiguration()
    {
        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:default:Features:DiagnosticsConsoleLogStreaming"] = ""
            })
            .Build();

        Assert.True(ConsoleLogStreamingFeature.IsFeatureEnabled(enabled));
        Assert.False(ConsoleLogStreamingFeature.IsFeatureEnabled(new ConfigurationBuilder().Build()));
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
    public void DoesNotEnableConsoleStreamHookWhenObjectFeatureIsDisabled()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": {
                "Shells": {
                  "default": {
                    "Features": {
                      "DiagnosticsConsoleLogStreaming": {
                        "Enabled": false
                      }
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
    public void InstallsConsoleStreamHookFromEnvironmentContentRootBeforeBuilderCreation()
    {
        ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        var originalContentRoot = Environment.GetEnvironmentVariable("DOTNET_CONTENTROOT");
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
            Environment.SetEnvironmentVariable("DOTNET_CONTENTROOT", contentRoot.FullName);

            ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled([], () => installCount++);

            Assert.Equal(1, installCount);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            Environment.SetEnvironmentVariable("DOTNET_CONTENTROOT", originalContentRoot);
            ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
            contentRoot.Delete(recursive: true);
            otherDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DoesNotInstallConsoleStreamHookWhenEnvironmentDisablesShellsJsonFeature()
    {
        ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();
        var originalDisableValue = Environment.GetEnvironmentVariable("CShells__Shells__default__Features__DiagnosticsConsoleLogStreaming");

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
            Environment.SetEnvironmentVariable("CShells__Shells__default__Features__DiagnosticsConsoleLogStreaming", "false");

            ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled(["--contentRoot", contentRoot.FullName], () => installCount++);

            Assert.Equal(0, installCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CShells__Shells__default__Features__DiagnosticsConsoleLogStreaming", originalDisableValue);
            ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
            contentRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void DoesNotInstallConsoleStreamHookWhenCommandLineDisablesShellsJsonFeature()
    {
        ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();

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

            ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled(
                [
                    "--contentRoot", contentRoot.FullName,
                    "--CShells:Shells:default:Features:DiagnosticsConsoleLogStreaming=false"
                ],
                () => installCount++);

            Assert.Equal(0, installCount);
        }
        finally
        {
            ConsoleLogStreamingFeature.ResetConsoleStreamHookInstallStateForTests();
            contentRoot.Delete(recursive: true);
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
