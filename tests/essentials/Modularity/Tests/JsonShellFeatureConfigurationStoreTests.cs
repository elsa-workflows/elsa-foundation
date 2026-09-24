using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const string SharedRevisionKey = "revision-key-shared-by-every-instance";
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
                },
                "Configuration": {
                  "Foo": {
                    "Bar": "baz"
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
    public async Task SavePreservesUnknownEnabledFeatureAndSettingValuesWhenRoundTripped()
    {
        await WriteDefaultFeaturesAsync("""
            {"FutureFeature":{"Flag":false,"Limit":0,"Label":"","Optional":null},"Existing":{"FutureSetting":{"Mode":"next"}}}
            """);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(_shellsPath))!.AsObject();
        document["Elsa"] = JsonNode.Parse("""{"Persistence":{"DefaultResource":"primary","Resources":{"primary":{"Provider":"PostgreSql","ConnectionName":"Shared"}}}}""");
        document["CShells"]!["Shells"]!["default"]!["Configuration"] =
            JsonNode.Parse("""{"Elsa":{"Persistence":{"Bindings":{"FutureFeature":"primary"}}}}""");
        await File.WriteAllTextAsync(_shellsPath, document.ToJsonString());
        var store = CreateStore();
        var snapshot = await store.LoadAsync();

        await store.SaveAsync(snapshot.Revision,
            snapshot.Features.Select(feature => new FeatureConfigurationChange(feature.Key, true, feature.Value)).ToArray());

        var reloaded = await store.LoadAsync();
        var future = reloaded.Features["FutureFeature"];
        Assert.False(future.GetProperty("Flag").GetBoolean());
        Assert.Equal(0, future.GetProperty("Limit").GetInt32());
        Assert.Equal("", future.GetProperty("Label").GetString());
        Assert.Equal(JsonValueKind.Null, future.GetProperty("Optional").ValueKind);
        Assert.Equal("next", reloaded.Features["Existing"].GetProperty("FutureSetting").GetProperty("Mode").GetString());
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(_shellsPath))!;
        Assert.Equal("Shared", (string?)persisted["Elsa"]?["Persistence"]?["Resources"]?["primary"]?["ConnectionName"]);
        Assert.Equal("primary", (string?)persisted["CShells"]?["Shells"]?["default"]?["Configuration"]?["Elsa"]?["Persistence"]?["Bindings"]?["FutureFeature"]);
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
    public async Task LoadCarriesTheShellsConfigurationNodeWithNestedShapeIntact()
    {
        var snapshot = await CreateStore("tenant-a").LoadAsync();

        Assert.Equal(JsonValueKind.Object, snapshot.Configuration.ValueKind);
        Assert.Equal("baz", snapshot.Configuration.GetProperty("Foo").GetProperty("Bar").GetString());
    }

    [Fact]
    public async Task LoadFallsBackToAnEmptyObjectWhenTheShellHasNoConfigurationNode()
    {
        var snapshot = await CreateStore("default").LoadAsync();

        Assert.Equal(JsonValueKind.Object, snapshot.Configuration.ValueKind);
        Assert.Empty(snapshot.Configuration.EnumerateObject());
    }

    [Fact]
    public async Task LoadResolvesTheConfigurationNodeUsingTheSameCaseInsensitiveShellIdAsFeatures()
    {
        var snapshot = await CreateStore("Tenant-A").LoadAsync();

        Assert.True(snapshot.Features.ContainsKey("TenantFeature"));
        Assert.Equal("baz", snapshot.Configuration.GetProperty("Foo").GetProperty("Bar").GetString());
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

    [Fact]
    public async Task ChangingASecretValueChangesTheRevision()
    {
        await WriteDefaultFeaturesAsync(WithSeedAdminPassword("hunter2"));
        var before = await CreateStore().LoadAsync();

        await WriteDefaultFeaturesAsync(WithSeedAdminPassword("hunter3"));

        Assert.NotEqual(before.Revision, (await CreateStore().LoadAsync()).Revision);
        await Assert.ThrowsAsync<FeatureCatalogRevisionConflictException>(() => CreateStore().SaveAsync(before.Revision, []));
    }

    [Fact]
    public async Task RevisionIsStableAcrossLoadsAndStoreInstances()
    {
        var store = CreateStore();
        var revision = (await store.LoadAsync()).Revision;

        Assert.Equal(revision, (await store.LoadAsync()).Revision);
        Assert.Equal(revision, (await CreateStore().LoadAsync()).Revision);
    }

    [Fact]
    public async Task InstancesSharingARevisionKeyAcceptEachOthersRevision()
    {
        var revision = (await CreateStore(revisionKey: SharedRevisionKey).LoadAsync()).Revision;

        Assert.Equal(revision, (await CreateStore(revisionKey: SharedRevisionKey).LoadAsync()).Revision);
        Assert.NotEqual(revision, (await CreateStore().LoadAsync()).Revision);
        await CreateStore(revisionKey: SharedRevisionKey).SaveAsync(revision, []);
    }

    [Fact]
    public async Task RevisionCannotBeRecomputedFromTheConfigurationWithoutTheKey()
    {
        await WriteDefaultFeaturesAsync(WithSeedAdminPassword("hunter2"));
        // Everything a catalog reader can rebuild, here with the secret guessed right.
        var features = await ReadDefaultFeaturesSectionAsync();

        var withProcessKey = await CreateStore().LoadAsync();
        var withSharedKey = await CreateStore(revisionKey: SharedRevisionKey).LoadAsync();

        var unkeyed = Convert.ToHexStringLower(SHA256.HashData(features));
        Assert.NotEqual(unkeyed, withProcessKey.Revision);
        Assert.NotEqual(unkeyed, withSharedKey.Revision);
        // The same input reproduces the revision once the key is known, so the key is the only thing the guess lacks.
        Assert.Equal(Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(SharedRevisionKey), features)), withSharedKey.Revision);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        return ValueTask.CompletedTask;
    }

    private JsonShellFeatureConfigurationStore CreateStore(string shellId = "default", string? revisionKey = null) =>
        new(
            new FakeEnvironment(_directory),
            new ShellSettings(new ShellId(shellId)),
            Options.Create(new FeatureManagementOptions
            {
                ShellsJsonPath = _shellsPath,
                RevisionKey = revisionKey
            }));

    private static string WithSeedAdminPassword(string password) =>
        $$$"""{"FoundationIdentityAspNetCoreIdentityEntityFrameworkCore":{"SeedAdminPassword":"{{{password}}}"},"ModularityApi":{}}""";

    private Task WriteDefaultFeaturesAsync(string features) =>
        File.WriteAllTextAsync(_shellsPath, """{"CShells":{"Shells":{"default":{"Features":""" + features + "}}}}");

    // The features section as the store reads it: parsed, then written indented with web defaults.
    private async Task<byte[]> ReadDefaultFeaturesSectionAsync()
    {
        var document = JsonNode.Parse(await File.ReadAllTextAsync(_shellsPath))!;
        var features = document["CShells"]!["Shells"]!["default"]!["Features"]!;
        return Encoding.UTF8.GetBytes(features.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

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
