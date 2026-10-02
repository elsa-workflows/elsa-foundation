using System.Reflection;
using CShells.DependencyInjection;
using CShells.Features;
using Elsa.Studio.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Loading;

namespace Elsa.Studio.Tests;

public sealed class WebHostShellFeatureDiscoveryTests : IAsyncLifetime
{
    // Features the Web host enables in shells.json but deliberately does not reference: their assemblies arrive through
    // an installed Nuplane package, so they only become discoverable once that package is dropped into the feed.
    private static readonly string[] PackageLoadedFeatures = ["WeatherForecastSample"];

    private HashSet<string> _enabled = null!;
    private HashSet<string> _discovered = null!;

    public async Task InitializeAsync()
    {
        _enabled = await ReadEnabledFeaturesAsync();
        _discovered = await DiscoverFeatureIdsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void EveryFeatureEnabledInShellsJson_ResolvesToDiscoverableFeatureType()
    {
        var undiscoverable = _enabled
            .Except(PackageLoadedFeatures, StringComparer.OrdinalIgnoreCase)
            .Where(feature => !_discovered.Contains(feature));

        Assert.NotEmpty(_enabled);
        Assert.Empty(undiscoverable);
    }

    [Fact]
    public void PackageLoadedFeatures_AreEnabledButNotInBox()
    {
        Assert.All(PackageLoadedFeatures, feature =>
        {
            Assert.Contains(feature, _enabled);
            Assert.DoesNotContain(feature, _discovered);
        });
    }

    // Reads the host's own shells.json through the store Feature Management uses, so "enabled" means exactly what it means at runtime.
    private static async Task<HashSet<string>> ReadEnabledFeaturesAsync()
    {
        var environment = new TestHostEnvironment(Path.Join(AppContext.BaseDirectory, "WebHost"));
        var store = new StudioShellFeatureConfigurationStore(environment, new ConfigurationBuilder().Build());
        var snapshot = await store.LoadAsync();
        return snapshot.Features.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // Uses the host's own assembly registration, with no Nuplane packages installed, so the result matches a fresh start.
    private static async Task<HashSet<string>> DiscoverFeatureIdsAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPackageAssemblyCatalog, EmptyPackageAssemblyCatalog>();
        services.AddSingleton<StudioNuplaneAssemblyProvider>();
        services.AddCShells(shells => shells.WithStudioFeatureAssemblies());
        await using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IRuntimeFeatureCatalog>().GetSnapshotAsync();
        return snapshot.FeatureDescriptors.Select(descriptor => descriptor.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class EmptyPackageAssemblyCatalog : IPackageAssemblyCatalog
    {
        public Task<IReadOnlyList<Assembly>> GetAssembliesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Assembly>>([]);

        public Task<IReadOnlyList<PackageAssemblies>> GetPackagedAssembliesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PackageAssemblies>>([]);

        public Task<PackageAssemblies?> GetPackagedAssembliesAsync(string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PackageAssemblies?>(null);
    }
}
