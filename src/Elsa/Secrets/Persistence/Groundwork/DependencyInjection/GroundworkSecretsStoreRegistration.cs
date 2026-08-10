using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Secrets.Persistence.Groundwork.DependencyInjection;

public static class GroundworkSecretsStoreRegistration
{
    public static IServiceCollection AddGroundworkSecretsStore(
        this IServiceCollection services,
        string? targetName = null)
    {
        var lane = services.GroundworkLane(targetName);
        lane.Manifest<SecretsGroundworkStorageManifestSource>();
        lane.Replace<ISecretRepository, GroundworkSecretRepository>();
        lane.TryAdd<ILegacySecretTenantBackfill, LegacySecretTenantBackfill>();
        return services;
    }
}
