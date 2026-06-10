using Elsa.Persistence.Groundwork.Diagnostics;
using Elsa.Persistence.Groundwork.Options;
using Elsa.Persistence.Groundwork.Providers;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Groundwork.Core.Manifests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.Groundwork.Tasks;

[SingleNodeTask]
[Order(-90)]
public sealed class MaterializeGroundworkStartupTask(
    IOptions<GroundworkPersistenceOptions> options,
    IEnumerable<IGroundworkPersistenceProvider> providers,
    GroundworkPersistenceDiagnosticStore diagnostics,
    ILogger<MaterializeGroundworkStartupTask> logger)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var manifestList = options.Value.Manifests.ToList();
        var providerList = providers.ToList();

        foreach (var manifest in manifestList)
        {
            if (providerList.Count == 0)
            {
                Record(manifest, null, GroundworkMaterializationStatus.ProviderUnavailable, "No Groundwork persistence providers are registered.");
                continue;
            }

            foreach (var provider in providerList)
            {
                if (!options.Value.MaterializeOnStartup)
                {
                    Record(manifest, provider, GroundworkMaterializationStatus.Skipped, "Groundwork startup materialization is disabled.");
                    continue;
                }

                try
                {
                    await provider.MaterializeAsync(manifest, cancellationToken);
                    Record(manifest, provider, GroundworkMaterializationStatus.Succeeded, null);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Groundwork materialization failed for manifest {ManifestIdentity} on provider {ProviderName}.", manifest.Identity.Value, provider.Identity.Name);
                    Record(manifest, provider, GroundworkMaterializationStatus.Failed, ex.Message);
                    throw;
                }
            }
        }
    }

    private void Record(StorageManifest manifest, IGroundworkPersistenceProvider? provider, GroundworkMaterializationStatus status, string? message) =>
        diagnostics.Record(new GroundworkMaterializationDiagnostic(
            manifest.Identity.Value,
            manifest.Version.Value,
            provider?.Identity.Name,
            provider?.Identity.Version,
            status,
            message,
            DateTimeOffset.UtcNow));
}
