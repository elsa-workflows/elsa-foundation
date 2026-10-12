using System.Reflection;
using CShells.Features;
using Nuplane.Loading;

namespace Elsa.Studio.Web;

internal sealed class StudioNuplaneAssemblyProvider(IPackageAssemblyCatalog packageAssemblyCatalog) : IFeatureAssemblyProvider
{
    public async Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        return await packageAssemblyCatalog.GetAssembliesAsync(cancellationToken);
    }
}
