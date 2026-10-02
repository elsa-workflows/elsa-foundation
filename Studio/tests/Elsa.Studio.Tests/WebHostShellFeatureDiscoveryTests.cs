using System.Reflection;
using CShells.DependencyInjection;
using CShells.Features;
using Elsa.Studio.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyModel;
using Nuplane.Loading;

namespace Elsa.Studio.Tests;

public sealed class WebHostShellFeatureDiscoveryTests : IAsyncLifetime
{
    // Features the Web host enables in shells.json but deliberately does not reference: their assemblies arrive through
    // an installed Nuplane package, so they only become discoverable once that package is dropped into the feed.
    private static readonly string[] PackageLoadedFeatures = ["WeatherForecastSample"];

    private HashSet<string> _enabled = null!;
    private HashSet<string> _shipped = null!;
    private HashSet<string> _discovered = null!;

    public async Task InitializeAsync()
    {
        _enabled = await ReadEnabledFeaturesAsync();
        _shipped = await DiscoverFeatureIdsAsync(shells => shells.WithAssemblies(LoadWebHostProjectAssemblies()));
        _discovered = await DiscoverFeatureIdsAsync(shells => shells.WithStudioFeatureAssemblies());
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

    // Covers opt-in features too (e.g. ExtensionBuilderStudio), which the host ships but leaves out of shells.json.
    [Fact]
    public void EveryFeatureTheWebHostShips_IsDiscoverable()
    {
        Assert.NotEmpty(_shipped);
        Assert.Empty(_shipped.Except(_discovered, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackageLoadedFeatures_AreEnabledButNotInBox()
    {
        Assert.All(PackageLoadedFeatures, feature =>
        {
            Assert.Contains(feature, _enabled);
            Assert.DoesNotContain(feature, _shipped);
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

    // The Web host's deps.json (copied beside the tests by Mvc.Testing) lists the projects the host is built from, so this
    // follows its ProjectReferences rather than the typeof(...) entries in StudioFeatureAssemblies that it is checked against.
    private static Assembly[] LoadWebHostProjectAssemblies()
    {
        using var stream = File.OpenRead(Path.Join(AppContext.BaseDirectory, "Elsa.Studio.Web.deps.json"));
        using var reader = new DependencyContextJsonReader();
        var context = reader.Read(stream);
        return context.RuntimeLibraries
            .Where(library => library.Type == "project")
            .SelectMany(library => library.GetDefaultAssemblyNames(context))
            .Select(Assembly.Load)
            .ToArray();
    }

    // Runs CShells discovery with no Nuplane packages installed, so the result matches a fresh start.
    private static async Task<HashSet<string>> DiscoverFeatureIdsAsync(Action<CShellsBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPackageAssemblyCatalog, EmptyPackageAssemblyCatalog>();
        services.AddSingleton<StudioNuplaneAssemblyProvider>();
        services.AddCShells(configure);
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
