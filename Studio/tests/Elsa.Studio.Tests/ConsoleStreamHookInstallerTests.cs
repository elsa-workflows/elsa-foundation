using System.Text;
using Elsa.Studio.ConsoleStream;
using Microsoft.Extensions.Configuration;

namespace Elsa.Studio.Tests;

public sealed class ConsoleStreamHookInstallerTests : IDisposable
{
    public ConsoleStreamHookInstallerTests() =>
        ConsoleStreamHookInstaller.ResetConsoleStreamHookInstallStateForTests();

    public void Dispose() =>
        ConsoleStreamHookInstaller.ResetConsoleStreamHookInstallStateForTests();

    [Fact]
    public void InstallsConsoleStreamHookOnlyWhenFeatureIsEnabled()
    {
        var installCount = 0;
        var disabledConfiguration = BuildConfiguration("StructuredLogsStudio");
        var enabledConfiguration = BuildConfiguration(ConsoleStreamHookInstaller.FeatureName);

        ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(disabledConfiguration, () => installCount++);
        ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(enabledConfiguration, () => installCount++);
        ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(enabledConfiguration, () => installCount++);

        Assert.Equal(1, installCount);
    }

    [Fact]
    public void DoesNotEnableConsoleStreamHookWhenFeatureIsMissingFromConfiguration()
    {
        var configuration = BuildConfiguration();

        Assert.False(ConsoleStreamHookInstaller.IsFeatureEnabled(configuration));
    }

    [Fact]
    public void DetectsEnabledFeatureFromEmptyObjectJsonShape()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": { "Shells": { "default": { "Features": { "ConsoleStream": {} } } } }
            }
            """);

        Assert.True(ConsoleStreamHookInstaller.IsFeatureEnabled(configuration));
    }

    [Fact]
    public void DoesNotEnableConsoleStreamHookWhenFeatureIsBooleanFalse()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": { "Shells": { "default": { "Features": { "ConsoleStream": false } } } }
            }
            """);

        Assert.False(ConsoleStreamHookInstaller.IsFeatureEnabled(configuration));
    }

    [Fact]
    public void DoesNotEnableConsoleStreamHookWhenObjectFeatureIsDisabled()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": { "Shells": { "default": { "Features": { "ConsoleStream": { "Enabled": false } } } } }
            }
            """);

        Assert.False(ConsoleStreamHookInstaller.IsFeatureEnabled(configuration));
    }

    [Fact]
    public void DetectsEnabledFeatureCaseInsensitively()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": { "Shells": { "default": { "Features": { "consolestream": {} } } } }
            }
            """);

        Assert.True(ConsoleStreamHookInstaller.IsFeatureEnabled(configuration));
    }

    [Fact]
    public void DetectsEnabledFeatureFromArrayJsonShape()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": { "Shells": { "default": { "Features": [ "ConsoleStream" ] } } }
            }
            """);

        Assert.True(ConsoleStreamHookInstaller.IsFeatureEnabled(configuration));
    }

    [Fact]
    public void DetectsEnabledFeatureFromArrayObjectJsonShape()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": { "Shells": { "default": { "Features": [ { "Name": "ConsoleStream", "EndpointPrefix": "/console" } ] } } }
            }
            """);

        Assert.True(ConsoleStreamHookInstaller.IsFeatureEnabled(configuration));
    }

    [Fact]
    public void DoesNotEnableConsoleStreamHookWhenArrayObjectFeatureIsDisabled()
    {
        var configuration = BuildJsonConfiguration("""
            {
              "CShells": { "Shells": { "default": { "Features": [ { "Name": "ConsoleStream", "Enabled": false } ] } } }
            }
            """);

        Assert.False(ConsoleStreamHookInstaller.IsFeatureEnabled(configuration));
    }

    [Fact]
    public void InstallsConsoleStreamHookFromContentRootBeforeBuilderCreation()
    {
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        var otherDirectory = Directory.CreateTempSubdirectory();

        try
        {
            WriteEnabledShellsJson(contentRoot.FullName);
            Directory.SetCurrentDirectory(otherDirectory.FullName);

            ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(["--contentRoot", contentRoot.FullName], () => installCount++);

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
    public void DoesNotInstallConsoleStreamHookWhenCommandLineDisablesShellsJsonFeature()
    {
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();

        try
        {
            WriteEnabledShellsJson(contentRoot.FullName);

            ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(
                [
                    "--contentRoot", contentRoot.FullName,
                    "--CShells:Shells:default:Features:ConsoleStream=false"
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
    public void DoesNotInstallConsoleStreamHookWhenShellsJsonIsMissing()
    {
        var installCount = 0;
        var contentRoot = Directory.CreateTempSubdirectory();

        try
        {
            ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(["--contentRoot", contentRoot.FullName], () => installCount++);

            Assert.Equal(0, installCount);
        }
        finally
        {
            contentRoot.Delete(recursive: true);
        }
    }

    private static void WriteEnabledShellsJson(string contentRoot) =>
        File.WriteAllText(Path.Combine(contentRoot, "shells.json"), """
            {
              "CShells": { "Shells": { "default": { "Features": { "ConsoleStream": {} } } } }
            }
            """);

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
