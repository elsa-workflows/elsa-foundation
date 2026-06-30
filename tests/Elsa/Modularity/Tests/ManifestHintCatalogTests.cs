using System.Text.Json;
using CShells.Features;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ManifestHintCatalogTests
{
    [Fact]
    public void ReaderReturnsSortedDedupedCategories()
    {
        var result = ManifestHintReader.Read(typeof(ManifestHintFixtureFeature));

        Assert.Equal(["Alpha", "Beta"], result.Categories);
    }

    [Fact]
    public void ReaderProjectsAnnotatedSettings()
    {
        var result = ManifestHintReader.Read(typeof(ManifestHintFixtureFeature));

        // Ignored + delegate-shaped properties are omitted; the two annotated settings remain, ordered by category then display name.
        Assert.Collection(result.Settings,
            buffer =>
            {
                Assert.Equal("BufferSize", buffer.Name);
                Assert.Equal("Buffer size", buffer.DisplayName);
                Assert.Equal("integer", buffer.JsonType);
                Assert.Equal("General", buffer.Category);
                Assert.Equal(2000, buffer.DefaultValue?.GetInt64());
            },
            mode =>
            {
                Assert.Equal("Mode", mode.Name);
                Assert.Equal("string", mode.JsonType);
                Assert.Equal("select-list", mode.UIHint);
                Assert.Equal(["Fast", "Slow"], mode.Options.Select(x => x.Value.GetString()));
            });
    }

    [Fact]
    public async Task ContributorPopulatesCategoriesAndSettingsFromStartupType()
    {
        var contributor = new RuntimeFeatureCatalogContributor(new FakeAccessor(
            new ShellFeatureDescriptor("ManifestHintFixture")
            {
                StartupType = typeof(ManifestHintFixtureFeature),
                Metadata = new Dictionary<string, object> { ["DisplayName"] = "Manifest Hint Fixture" }
            }));
        var context = CreateContext();

        await contributor.ContributeAsync(context);

        var item = context.Items["ManifestHintFixture"].ToItem();
        Assert.Equal(FeatureSourceKinds.Runtime, item.SourceKind);
        Assert.Equal(["Alpha", "Beta"], item.Categories);
        Assert.Equal(2, item.Settings.Count);
    }

    [Fact]
    public async Task ContributorDoesNotOverwriteManifestSourcedMetadata()
    {
        var contributor = new RuntimeFeatureCatalogContributor(new FakeAccessor(
            new ShellFeatureDescriptor("ManifestHintFixture")
            {
                StartupType = typeof(ManifestHintFixtureFeature)
            }));
        var context = CreateContext();
        var seeded = context.GetOrAdd("ManifestHintFixture");
        seeded.SourceKind = FeatureSourceKinds.Manifest;
        seeded.Categories = ["FromManifest"];

        await contributor.ContributeAsync(context);

        var item = context.Items["ManifestHintFixture"].ToItem();
        Assert.Equal(FeatureSourceKinds.Manifest, item.SourceKind);
        Assert.Equal(["FromManifest"], item.Categories);
        Assert.Empty(item.Settings);
    }

    private static FeatureCatalogContributionContext CreateContext() =>
        new(new ShellFeatureConfigurationSnapshot("default", "revision", new Dictionary<string, JsonElement>()));

    private sealed class FakeAccessor(params ShellFeatureDescriptor[] descriptors) : IRuntimeFeatureCatalog
    {
        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Build());

        public Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Build());

        private RuntimeFeatureCatalogSnapshot Build()
        {
            var map = new Dictionary<string, ShellFeatureDescriptor>(StringComparer.OrdinalIgnoreCase);
            foreach (var descriptor in descriptors)
                if (!string.IsNullOrWhiteSpace(descriptor.Id))
                    map[descriptor.Id] = descriptor;

            return new RuntimeFeatureCatalogSnapshot(1, [], descriptors, map, DateTimeOffset.UnixEpoch);
        }
    }
}

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Beta")]
[ManifestFeatureCategory("Alpha")]
[ManifestFeatureCategory("Alpha")]
[ShellFeature(
    name: "ManifestHintFixture",
    DisplayName = "Manifest Hint Fixture",
    Description = "Fixture feature exercising manifest hint reflection."
)]
public sealed class ManifestHintFixtureFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Buffer size", Description = "Entries retained in memory.", Category = "General", DefaultValue = "2000")]
    public int BufferSize { get; set; } = 2000;

    [ManifestSetting(DisplayName = "Mode", Category = "General")]
    public ManifestHintFixtureMode Mode { get; set; } = ManifestHintFixtureMode.Fast;

    [ManifestIgnore]
    public string Hidden { get; set; } = "secret";

    public Action<int>? Hook { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
    }
}

public enum ManifestHintFixtureMode
{
    Fast,
    Slow
}
