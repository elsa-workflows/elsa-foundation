using System.Reflection;
using CShells.Features;
using Nuplane.Loading;

namespace Elsa.Foundation.Host.Feed;

/// <summary>
/// The bridge between Nuplane and CShells. CShells calls this to learn which assemblies to scan for shell
/// features; we return every assembly the Nuplane package feed has loaded. This is the single glue seam that
/// makes a feed-driven, feature-free host possible: no feature is compiled into the host, yet CShells still
/// discovers all of them.
/// </summary>
internal sealed class NuplaneAssemblyProvider(IPackageAssemblyCatalog packageAssemblyCatalog) : IFeatureAssemblyProvider
{
    public async Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        return await packageAssemblyCatalog.GetAssembliesAsync(cancellationToken);
    }
}
