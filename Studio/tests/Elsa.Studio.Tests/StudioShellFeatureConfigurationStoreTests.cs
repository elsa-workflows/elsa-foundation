using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Studio.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Studio.Tests;

public sealed class StudioShellFeatureConfigurationStoreTests : IDisposable
{
    private readonly string _contentRoot = Directory.CreateTempSubdirectory("studio-shell-config").FullName;
    private readonly string _shellsPath;

    public StudioShellFeatureConfigurationStoreTests()
    {
        _shellsPath = Path.Combine(_contentRoot, "shells.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task SaveAsync_WritesFeaturesAndRoundTrips()
    {
        WriteShells(SeedWithFeatures(new JsonObject()));
        var store = CreateStore(out _);

        var initial = await store.LoadAsync();
        var updated = await store.SaveAsync(initial.Revision,
        [
            Enabled("Elsa.Studio.Alpha"),
            Enabled("Elsa.Studio.Beta", new JsonObject { ["Threshold"] = 3 }),
            Disabled("Elsa.Studio.Gamma")
        ]);

        Assert.Contains("Elsa.Studio.Alpha", updated.Features.Keys);
        Assert.Contains("Elsa.Studio.Beta", updated.Features.Keys);
        Assert.DoesNotContain("Elsa.Studio.Gamma", updated.Features.Keys);

        // Persisted content is valid JSON with the expected feature nodes.
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(_shellsPath))!.AsObject();
        var features = persisted["CShells"]!["Shells"]!["Default"]!["Features"]!.AsObject();
        Assert.Equal(3, (int)features["Elsa.Studio.Beta"]!["Threshold"]!);
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTempFilesBehind()
    {
        WriteShells(SeedWithFeatures(new JsonObject()));
        var store = CreateStore(out _);

        var initial = await store.LoadAsync();
        await store.SaveAsync(initial.Revision, [Enabled("Elsa.Studio.Alpha")]);

        var strays = Directory.EnumerateFiles(_contentRoot)
            .Where(path => !string.Equals(Path.GetFileName(path), "shells.json", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(strays);
    }

    [Fact]
    public async Task SaveAsync_ThrowsOnRevisionConflictAndLeavesFileIntact()
    {
        WriteShells(SeedWithFeatures(new JsonObject()));
        var store = CreateStore(out _);

        var before = await File.ReadAllTextAsync(_shellsPath);

        await Assert.ThrowsAsync<StudioFeatureCatalogRevisionConflictException>(() =>
            store.SaveAsync("not-the-current-revision", [Enabled("Elsa.Studio.Alpha")]));

        // A rejected save must not have touched the file at all.
        Assert.Equal(before, await File.ReadAllTextAsync(_shellsPath));
    }

    [Fact]
    public async Task SaveAsync_ReloadsConfigurationRootSoFreshValuesAreVisible()
    {
        WriteShells(SeedWithFeatures(new JsonObject()));
        var store = CreateStore(out var configuration);

        var initial = await store.LoadAsync();
        await store.SaveAsync(initial.Revision, [Enabled("Elsa.Studio.Alpha", new JsonObject { ["Level"] = "high" })]);

        // The explicit reload in SaveAsync must make the new value observable immediately, without
        // waiting on the (asynchronous, debounced) reloadOnChange file watcher.
        Assert.Equal("high", configuration["CShells:Shells:Default:Features:Elsa.Studio.Alpha:Level"]);
    }

    [Fact]
    public async Task LoadAsync_CoercesUnexpectedScalarToEnabledAndWarns()
    {
        // A non-bool, non-object scalar is an authoring mistake but must not silently disable the feature.
        WriteShells(new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    ["Default"] = new JsonObject
                    {
                        ["Features"] = new JsonObject
                        {
                            ["Elsa.Studio.Weird"] = "oops"
                        }
                    }
                }
            }
        });

        var logger = new RecordingLogger<StudioShellFeatureConfigurationStore>();
        var store = CreateStore(out _, logger);

        var snapshot = await store.LoadAsync();

        Assert.Contains("Elsa.Studio.Weird", snapshot.Features.Keys);
        Assert.Equal(JsonValueKind.Object, snapshot.Features["Elsa.Studio.Weird"].ValueKind);
        Assert.Contains(logger.Warnings, message => message.Contains("Elsa.Studio.Weird") && message.Contains("String"));
    }

    [Fact]
    public async Task LoadAsync_HonorsBoolPrecedence()
    {
        WriteShells(new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    ["Default"] = new JsonObject
                    {
                        ["Features"] = new JsonObject
                        {
                            ["Elsa.Studio.On"] = true,
                            ["Elsa.Studio.Off"] = false,
                            ["Elsa.Studio.Configured"] = new JsonObject { ["Size"] = 10 }
                        }
                    }
                }
            }
        });

        var store = CreateStore(out _);
        var snapshot = await store.LoadAsync();

        Assert.Contains("Elsa.Studio.On", snapshot.Features.Keys);
        Assert.Equal(JsonValueKind.Object, snapshot.Features["Elsa.Studio.On"].ValueKind);
        Assert.DoesNotContain("Elsa.Studio.Off", snapshot.Features.Keys);
        Assert.Equal(10, snapshot.Features["Elsa.Studio.Configured"].GetProperty("Size").GetInt32());
    }

    private static FeatureManagementApplyItemRequest Enabled(string id, JsonNode? configuration = null) =>
        new(id, true, ToElement(configuration ?? new JsonObject()));

    private static FeatureManagementApplyItemRequest Disabled(string id) =>
        new(id, false, ToElement(new JsonObject()));

    private static JsonElement ToElement(JsonNode node) =>
        JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());

    private void WriteShells(JsonObject document) =>
        File.WriteAllText(_shellsPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    // Wraps a Features object in the CShells:Shells:Default:Features skeleton the store expects to exist.
    private static JsonObject SeedWithFeatures(JsonObject features) =>
        new()
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    ["Default"] = new JsonObject
                    {
                        ["Features"] = features
                    }
                }
            }
        };

    private StudioShellFeatureConfigurationStore CreateStore(
        out IConfigurationRoot configuration,
        ILogger<StudioShellFeatureConfigurationStore>? logger = null)
    {
        configuration = new ConfigurationBuilder()
            .AddJsonFile(_shellsPath, optional: false, reloadOnChange: false)
            .Build();

        return new StudioShellFeatureConfigurationStore(
            new TestHostEnvironment(_contentRoot),
            configuration,
            logger ?? NullLogger<StudioShellFeatureConfigurationStore>.Instance);
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Elsa.Studio.Tests";
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
