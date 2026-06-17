using System.Reflection;
using CShells.Features;

namespace Elsa.Modularity.Nuplane.Services;

public sealed class RuntimeFeatureCatalogAccessor(
    IEnumerable<IFeatureAssemblyProvider> assemblyProviders,
    IServiceProvider serviceProvider) : IRuntimeFeatureCatalogAccessor
{
    public async Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var assemblies = new List<Assembly>();
        foreach (var provider in assemblyProviders)
        {
            var providerAssemblies = await provider.GetAssembliesAsync(serviceProvider, cancellationToken).ConfigureAwait(false);
            assemblies.AddRange(providerAssemblies);
        }

        var distinctAssemblies = assemblies
            .GroupBy(assembly => assembly.FullName, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var descriptors = FeatureDiscovery
            .DiscoverFeatures(distinctAssemblies, ThrowDiscoveryError)
            .ToArray();

        return new RuntimeFeatureCatalogSnapshot(descriptors);
    }

    private static void ThrowDiscoveryError(Assembly assembly, Exception exception) =>
        throw new InvalidOperationException($"Failed to discover CShells features from assembly '{assembly.FullName}'.", exception);
}
