using System.Text.Json;
using CShells;
using Elsa.Modularity.Api.Options;
using Elsa.Modularity.Api.Services;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class JsonShellFeatureConfigurationStoreTests : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-modularity-{Guid.NewGuid():N}");
    private readonly string _shellsPath;

    public JsonShellFeatureConfigurationStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _shellsPath = Path.Combine(_directory, "shells.json");
        File.WriteAllText(_shellsPath, """
        {
          "CShells": {
            "Shells": {
              "default": {
                "Features": {
                  "Existing": {
                    "Enabled": true
                  },
                  "Disabled": false
                }
              },
              "tenant-a": {
                "Features": {
                  "TenantFeature": {
                    "Value": "tenant"
                  }
                }
              }
            }
          }
        }
        """);
    }

    [Fact]
    public async Task LoadReturnsEnabledFeaturesAndRevision()
    {
        var snapshot = await CreateStore().LoadAsync();

        Assert.NotEmpty(snapshot.Revision);
        Assert.True(snapshot.Features.ContainsKey("Existing"));
        Assert.False(snapshot.Features.ContainsKey("Disabled"));
        Assert.True(snapshot.Features["Existing"].GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public async Task SaveReplacesFeatureMapWithEnabledFeatures()
    {
        var store = CreateStore();
        var snapshot = await store.LoadAsync();

        await store.SaveAsync(snapshot.Revision,
        [
            new FeatureConfigurationChange("Existing", false, Json("{}")),
            new FeatureConfigurationChange("NewFeature", true, Json("""{"value":"abc"}"""))
        ]);

        var updated = await store.LoadAsync();
        Assert.False(updated.Features.ContainsKey("Existing"));
        Assert.Equal("abc", updated.Features["NewFeature"].GetProperty("value").GetString());
    }

    [Fact]
    public async Task SaveRejectsStaleRevision()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<FeatureCatalogRevisionConflictException>(() =>
            store.SaveAsync("stale", []));
    }

    [Fact]
    public async Task LoadUsesResolvedShellSettingsId()
    {
        var snapshot = await CreateStore("tenant-a").LoadAsync();

        Assert.Equal("tenant-a", snapshot.ShellId);
        Assert.True(snapshot.Features.ContainsKey("TenantFeature"));
        Assert.False(snapshot.Features.ContainsKey("Existing"));
    }

    [Fact]
    public async Task LoadRejectsMissingShellConfigurationFile()
    {
        File.Delete(_shellsPath);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateStore().LoadAsync());

        Assert.Contains("Could not find shell configuration file", error.Message);
    }

    [Fact]
    public async Task LoadRejectsShellConfigurationWithoutFeatures()
    {
        await File.WriteAllTextAsync(_shellsPath, """{"CShells":{"Shells":{"default":{}}}}""");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateStore().LoadAsync());

        Assert.Contains("Features", error.Message);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        return ValueTask.CompletedTask;
    }

    private JsonShellFeatureConfigurationStore CreateStore(string shellId = "default") =>
        new(
            new FakeEnvironment(_directory),
            new ShellSettings(new ShellId(shellId)),
            Options.Create(new FeatureManagementOptions
            {
                ShellsJsonPath = _shellsPath
            }));

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
