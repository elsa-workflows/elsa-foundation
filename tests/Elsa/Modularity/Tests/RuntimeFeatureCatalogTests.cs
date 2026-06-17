using System.Reflection;
using System.Text.Json;
using CShells.Features;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class RuntimeFeatureCatalogTests
{
    [Fact]
    public async Task AccessorDiscoversFeatureDescriptorsFromAssemblyProviders()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var assemblyProvider = new TestFeatureAssemblyProvider(typeof(RuntimeCatalogTestFeature).Assembly);
        var accessor = new RuntimeFeatureCatalogAccessor([assemblyProvider], serviceProvider);

        var snapshot = await accessor.RefreshAsync();

        var descriptor = Assert.Single(snapshot.FeatureDescriptors, x => x.Id == "RuntimeCatalogTestFeature");
        Assert.Same(serviceProvider, assemblyProvider.ServiceProvider);
        Assert.Equal("Runtime Catalog Test Feature", descriptor.Metadata["DisplayName"]);
        Assert.Equal("Runtime catalog test feature description.", descriptor.Metadata["Description"]);
    }

    [Fact]
    public async Task ContributorAddsRuntimeFeatureDescriptorMetadata()
    {
        var contributor = new RuntimeFeatureCatalogContributor(new FakeRuntimeFeatureCatalogAccessor(
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
    public async Task ContributorDoesNotReplaceManifestSourceKind()
    {
        var contributor = new RuntimeFeatureCatalogContributor(new FakeRuntimeFeatureCatalogAccessor(
            new ShellFeatureDescriptor("ManifestFeature")));
        var context = CreateContext();
        context.GetOrAdd("ManifestFeature").SourceKind = FeatureSourceKinds.Manifest;

        await contributor.ContributeAsync(context);

        Assert.Equal(FeatureSourceKinds.Manifest, context.Items["ManifestFeature"].SourceKind);
    }

    [Fact]
    public async Task ContributorSkipsDescriptorsWithoutFeatureIds()
    {
        var contributor = new RuntimeFeatureCatalogContributor(new FakeRuntimeFeatureCatalogAccessor(
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
        var accessor = new FakeRuntimeFeatureCatalogAccessor(
            new ShellFeatureDescriptor("FeatureOne"),
            new ShellFeatureDescriptor("FeatureTwo"));
        var refresher = new RuntimeFeatureCatalogRefresher(accessor);

        var count = await refresher.RefreshAsync();

        Assert.Equal(2, count);
        Assert.Equal(1, accessor.RefreshCount);
    }

    private static FeatureCatalogContributionContext CreateContext() =>
        new(new ShellFeatureConfigurationSnapshot("default", "revision", new Dictionary<string, JsonElement>()));

    private sealed class FakeRuntimeFeatureCatalogAccessor(params ShellFeatureDescriptor[] descriptors) : IRuntimeFeatureCatalogAccessor
    {
        public int RefreshCount { get; private set; }

        public Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.FromResult(new RuntimeFeatureCatalogSnapshot(descriptors));
        }
    }

    private sealed class TestFeatureAssemblyProvider(params Assembly[] assemblies) : IFeatureAssemblyProvider
    {
        public IServiceProvider? ServiceProvider { get; private set; }

        public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            ServiceProvider = serviceProvider;
            return Task.FromResult(assemblies.AsEnumerable());
        }
    }
}

[ShellFeature(
    name: "RuntimeCatalogTestFeature",
    DisplayName = "Runtime Catalog Test Feature",
    Description = "Runtime catalog test feature description."
)]
public sealed class RuntimeCatalogTestFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
