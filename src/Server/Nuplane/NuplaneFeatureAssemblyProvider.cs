using CShells.Features;
using Nuplane.Loading;

namespace Elsa.Server.Nuplane
{
    public class NuplaneFeatureAssemblyProvider(IPackageAssemblyCatalog packageAssemblyCatalog) : IFeatureAssemblyProvider
    {
        public async Task<IEnumerable<System.Reflection.Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var loadedPackages = await packageAssemblyCatalog.GetPackagedAssembliesAsync(cancellationToken);
            return loadedPackages.SelectMany(x => x.Assemblies).Distinct();
        }
    }
}
