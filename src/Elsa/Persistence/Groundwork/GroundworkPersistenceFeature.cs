using CShells.Features;
using Elsa.Persistence.Groundwork.Extensions;
using Groundwork.Core.Manifests;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork;

[ShellFeature(
    name: "GroundworkPersistence",
    DisplayName = "Groundwork Persistence",
    Description = "Registers opt-in Groundwork persistence integration for Elsa."
)]
public sealed class GroundworkPersistenceFeature : IShellFeature
{
    public IList<StorageManifest> Manifests { get; } = [];
    public bool MaterializeOnStartup { get; set; } = true;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaGroundworkPersistence(options =>
        {
            options.MaterializeOnStartup = MaterializeOnStartup;

            foreach (var manifest in Manifests)
                options.Manifests.Add(manifest);
        });
    }
}
