using System.Text;
using Elsa.Studio.ConsoleStream;
using Microsoft.Extensions.Configuration;

namespace Elsa.Studio.Tests;

public sealed class ConsoleStreamHookInstallerTests : IDisposable
{
    private readonly List<DirectoryInfo> _tempDirs = [];

    public ConsoleStreamHookInstallerTests() =>
        ConsoleStreamHookInstaller.ResetConsoleStreamHookInstallStateForTests();

    public void Dispose()
    {
        ConsoleStreamHookInstaller.ResetConsoleStreamHookInstallStateForTests();

        foreach (var dir in _tempDirs)
            dir.Delete(recursive: true);
    }

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

    // IsFeatureEnabled must recognize the feature across every shells.json shape CShells accepts
    // (object/array, empty/named, with or without an explicit Enabled flag) and case-insensitively,
    // while treating a missing entry, boolean false, or an explicit Enabled:false as disabled.
    [Theory]
    [MemberData(nameof(FeatureShapeCases))]
    public void IsFeatureEnabledReflectsShellsJsonShape(string json, bool expected)
    {
        var configuration = BuildJsonConfiguration(json);

        Assert.Equal(expected, ConsoleStreamHookInstaller.IsFeatureEnabled(configuration));
    }

    public static TheoryData<string, bool> FeatureShapeCases() => new()
    {
        // Missing from configuration.
        { "{}", false },
        // Object shapes.
        { """{ "CShells": { "Shells": { "default": { "Features": { "ConsoleStream": {} } } } } }""", true },
        { """{ "CShells": { "Shells": { "default": { "Features": { "ConsoleStream": false } } } } }""", false },
        { """{ "CShells": { "Shells": { "default": { "Features": { "ConsoleStream": { "Enabled": false } } } } } }""", false },
        // Case-insensitive feature name.
        { """{ "CShells": { "Shells": { "default": { "Features": { "consolestream": {} } } } } }""", true },
        // Array shapes.
        { """{ "CShells": { "Shells": { "default": { "Features": [ "ConsoleStream" ] } } } }""", true },
        { """{ "CShells": { "Shells": { "default": { "Features": [ { "Name": "ConsoleStream", "EndpointPrefix": "/console" } ] } } } }""", true },
        { """{ "CShells": { "Shells": { "default": { "Features": [ { "Name": "ConsoleStream", "Enabled": false } ] } } } }""", false },
    };

    [Fact]
    public void InstallsConsoleStreamHookFromContentRootBeforeBuilderCreation()
    {
        var installCount = 0;
        var contentRoot = CreateTempDir();
        var otherDirectory = CreateTempDir();
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        WriteEnabledShellsJson(contentRoot.FullName);

        try
        {
            Directory.SetCurrentDirectory(otherDirectory.FullName);

            ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(["--contentRoot", contentRoot.FullName], () => installCount++);

            Assert.Equal(1, installCount);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
        }
    }

    [Fact]
    public void DoesNotInstallConsoleStreamHookWhenCommandLineDisablesShellsJsonFeature()
    {
        var installCount = 0;
        var contentRoot = CreateTempDir();
        WriteEnabledShellsJson(contentRoot.FullName);

        ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(
            [
                "--contentRoot", contentRoot.FullName,
                "--CShells:Shells:default:Features:ConsoleStream=false"
            ],
            () => installCount++);

        Assert.Equal(0, installCount);
    }

    [Fact]
    public void DoesNotInstallConsoleStreamHookWhenShellsJsonIsMissing()
    {
        var installCount = 0;
        var contentRoot = CreateTempDir();

        ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(["--contentRoot", contentRoot.FullName], () => installCount++);

        Assert.Equal(0, installCount);
    }

    private DirectoryInfo CreateTempDir()
    {
        var dir = Directory.CreateTempSubdirectory();
        _tempDirs.Add(dir);
        return dir;
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
