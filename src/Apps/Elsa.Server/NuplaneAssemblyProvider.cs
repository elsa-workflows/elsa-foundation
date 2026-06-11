using System.Reflection;
using CShells.Features;
using Nuplane.Loading;

namespace Elsa.Server;

internal sealed class NuplaneAssemblyProvider(IPackageAssemblyCatalog packageAssemblyCatalog) : IFeatureAssemblyProvider
{
    public async Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        return await packageAssemblyCatalog.GetAssembliesAsync(cancellationToken);
    }
}
