using ConsoleLogStreaming.AspNetCore;
using ConsoleLogStreaming.Core;
using ConsoleLogStreaming.Core.Capture;
using ConsoleLogStreaming.Core.Options;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Diagnostics.ConsoleLogStreaming.Tests;

public sealed class ConsoleLogStreamingSetupTests : IDisposable
{
    private const string EnabledKey = ConsoleLogStreamingSetup.EnabledConfigKey;

    public ConsoleLogStreamingSetupTests() => ResetState();

    public void Dispose() => ResetState();

    private static void ResetState()
    {
        ConsoleLogStreamingHost.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
        ConsoleStreamHook.Uninstall();
        ConsoleLogStreamingSetup.ResetConsoleStreamHookInstallStateForTests();
    }

    [Fact]
    public void ConfigureHostAppliesDiagnosticsDefaults()
    {
        var options = new ConsoleLogOptions();

        ConsoleLogStreamingSetup.ConfigureHost(options);

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

        ConsoleLogStreamingSetup.ConfigureEndpoints(options);

        Assert.Equal(ConsoleLogStreamingSetup.DefaultRecentPath, options.RecentPath);
        Assert.Equal(ConsoleLogStreamingSetup.DefaultSourcesPath, options.SourcesPath);
        Assert.Equal(ConsoleLogStreamingSetup.DefaultHubPath, options.HubPath);
    }

    [Fact]
    public void DefaultRoutesShareTheDiagnosticsEndpointPrefix()
    {
        // The Studio ConsoleStream client targets these fixed paths; pin them so a refactor can't silently move the
        // host-mapped routes out from under the client.
        Assert.Equal("/_elsa/server/diagnostics/console-logs/recent", ConsoleLogStreamingSetup.DefaultRecentPath);
        Assert.Equal("/_elsa/server/diagnostics/console-logs/sources", ConsoleLogStreamingSetup.DefaultSourcesPath);
        Assert.Equal("/_elsa/server/diagnostics/console-logs/hub", ConsoleLogStreamingSetup.DefaultHubPath);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("", false)]     // present but unparseable → off
    [InlineData(null, false)]   // absent → off (fail-closed)
    public void IsEnabledReflectsTheConfigSwitch(string? value, bool expected)
    {
        var settings = new Dictionary<string, string?>();
        if (value is not null)
            settings[EnabledKey] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        Assert.Equal(expected, ConsoleLogStreamingSetup.IsEnabled(configuration));
    }

    [Fact]
    public void InstallsConsoleStreamHookOnlyWhenEnabledAndOnlyOnce()
    {
        var installCount = 0;
        var disabled = BuildConfiguration(false);
        var enabled = BuildConfiguration(true);

        ConsoleLogStreamingSetup.InstallConsoleStreamHookIfEnabled(disabled, () => installCount++);
        ConsoleLogStreamingSetup.InstallConsoleStreamHookIfEnabled(enabled, () => installCount++);
        ConsoleLogStreamingSetup.InstallConsoleStreamHookIfEnabled(enabled, () => installCount++);

        Assert.Equal(1, installCount);
    }

    [Fact]
    public void InstallsConsoleStreamHookFromContentRootAppSettingsBeforeBuilderCreation()
    {
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        var otherDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WriteAppSettings(contentRoot.FullName, enabled: true);
            Directory.SetCurrentDirectory(otherDirectory.FullName);

            ConsoleLogStreamingSetup.InstallConsoleStreamHookIfEnabled(["--contentRoot", contentRoot.FullName], () => installCount++);

            Assert.Equal(1, installCount);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            contentRoot.Delete(recursive: true);
            otherDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InstallsConsoleStreamHookFromEnvironmentContentRootBeforeBuilderCreation()
    {
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        var originalContentRoot = Environment.GetEnvironmentVariable("DOTNET_CONTENTROOT");
        var otherDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WriteAppSettings(contentRoot.FullName, enabled: true);
            Directory.SetCurrentDirectory(otherDirectory.FullName);
            Environment.SetEnvironmentVariable("DOTNET_CONTENTROOT", contentRoot.FullName);

            ConsoleLogStreamingSetup.InstallConsoleStreamHookIfEnabled([], () => installCount++);

            Assert.Equal(1, installCount);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            Environment.SetEnvironmentVariable("DOTNET_CONTENTROOT", originalContentRoot);
            contentRoot.Delete(recursive: true);
            otherDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DoesNotInstallConsoleStreamHookWhenEnvironmentDisablesTheSwitch()
    {
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();
        const string envKey = "Elsa__Diagnostics__ConsoleLogStreaming__Enabled";
        var originalDisableValue = Environment.GetEnvironmentVariable(envKey);

        try
        {
            WriteAppSettings(contentRoot.FullName, enabled: true);
            Environment.SetEnvironmentVariable(envKey, "false");

            ConsoleLogStreamingSetup.InstallConsoleStreamHookIfEnabled(["--contentRoot", contentRoot.FullName], () => installCount++);

            Assert.Equal(0, installCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, originalDisableValue);
            contentRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void DoesNotInstallConsoleStreamHookWhenCommandLineDisablesTheSwitch()
    {
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();

        try
        {
            WriteAppSettings(contentRoot.FullName, enabled: true);

            ConsoleLogStreamingSetup.InstallConsoleStreamHookIfEnabled(
                [
                    "--contentRoot", contentRoot.FullName,
                    "--Elsa:Diagnostics:ConsoleLogStreaming:Enabled=false"
                ],
                () => installCount++);

            Assert.Equal(0, installCount);
        }
        finally
        {
            contentRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void DoesNotInstallConsoleStreamHookWhenAppSettingsIsAbsent()
    {
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();

        try
        {
            // No appsettings.json written → switch absent → off (fail-closed).
            ConsoleLogStreamingSetup.InstallConsoleStreamHookIfEnabled(["--contentRoot", contentRoot.FullName], () => installCount++);

            Assert.Equal(0, installCount);
        }
        finally
        {
            contentRoot.Delete(recursive: true);
        }
    }

    private static IConfiguration BuildConfiguration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [EnabledKey] = enabled ? "true" : "false" })
            .Build();

    private static void WriteAppSettings(string contentRoot, bool enabled) =>
        File.WriteAllText(Path.Combine(contentRoot, "appsettings.json"), $$"""
            {
              "Elsa": {
                "Diagnostics": {
                  "ConsoleLogStreaming": {
                    "Enabled": {{(enabled ? "true" : "false")}}
                  }
                }
              }
            }
            """);
}
