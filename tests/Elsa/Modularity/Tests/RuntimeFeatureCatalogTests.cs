using System.Text.Json;
using CShells.Features;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class RuntimeFeatureCatalogTests
{
    [Fact]
    public async Task ContributorAddsRuntimeFeatureDescriptorMetadata()
    {
        var contributor = new RuntimeFeatureCatalogContributor(new FakeRuntimeFeatureCatalog(
            new ShellFeatureDescriptor("RuntimeFeature")
            {
                Metadata = new Dictionary<string, object>
                {
                    ["DisplayName"] = "Runtime Feature",
                    ["Description"] = "Runtime feature description."
                }
            }));
        var context = CreateContext();

        await contributor.ContributeAsync(context);

        var item = context.Items["RuntimeFeature"].ToItem();
        Assert.Equal(FeatureSourceKinds.Runtime, item.SourceKind);
        Assert.Equal("Runtime Feature", item.DisplayName);
        Assert.Equal("Runtime feature description.", item.Description);
    }

    [Fact]
    public async Task ContributorSurfacesFeatureDependencies()
    {
        var contributor = new RuntimeFeatureCatalogContributor(new FakeRuntimeFeatureCatalog(
            new ShellFeatureDescriptor("RuntimeFeature")
            {
                Dependencies = ["DependencyOne", "DependencyTwo"]
            }));
        var context = CreateContext();

        await contributor.ContributeAsync(context);

        var item = context.Items["RuntimeFeature"].ToItem();
        Assert.Equal(["DependencyOne", "DependencyTwo"], item.Dependencies);
    }

    [Fact]
    public async Task ContributorDoesNotReplaceManifestSourceKind()
    {
        var contributor = new RuntimeFeatureCatalogContributor(new FakeRuntimeFeatureCatalog(
            new ShellFeatureDescriptor("ManifestFeature")));
        var context = CreateContext();
        context.GetOrAdd("ManifestFeature").SourceKind = FeatureSourceKinds.Manifest;

        await contributor.ContributeAsync(context);

        Assert.Equal(FeatureSourceKinds.Manifest, context.Items["ManifestFeature"].SourceKind);
    }

    [Fact]
    public async Task ContributorSkipsDescriptorsWithoutFeatureIds()
    {
        var contributor = new RuntimeFeatureCatalogContributor(new FakeRuntimeFeatureCatalog(
            new ShellFeatureDescriptor
            {
                Id = " "
            }));
        var context = CreateContext();

        await contributor.ContributeAsync(context);

        Assert.Empty(context.Items);
    }

    [Fact]
    public async Task RefresherReturnsDescriptorCount()
    {
        var catalog = new FakeRuntimeFeatureCatalog(
            new ShellFeatureDescriptor("FeatureOne"),
            new ShellFeatureDescriptor("FeatureTwo"));
        var refresher = new RuntimeFeatureCatalogRefresher(catalog);

        var count = await refresher.RefreshAsync();

        Assert.Equal(2, count);
        Assert.Equal(1, catalog.RefreshCount);
    }

    private static FeatureCatalogContributionContext CreateContext() =>
        new(new ShellFeatureConfigurationSnapshot("default", "revision", new Dictionary<string, JsonElement>()));

    private sealed class FakeRuntimeFeatureCatalog(params ShellFeatureDescriptor[] descriptors) : IRuntimeFeatureCatalog
    {
        public int RefreshCount { get; private set; }

        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Build());

        public Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.FromResult(Build());
        }

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
