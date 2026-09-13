using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Persistence.Groundwork.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Replaces the default in-memory identity stores with Groundwork-backed, durable stores so users, roles,
/// external identities, and tenant memberships survive process restarts. Each store is registered as its
/// own scoped service (unlike the in-memory store, which is a single object cast to four interfaces) so
/// immutable persistence access and operation state cannot cross request boundaries.
/// </summary>
public static class GroundworkIdentityStoresRegistration
{
    public static IServiceCollection AddGroundworkIdentityStores(this IServiceCollection services)
    {
        var existingProviderConfigurationBackend = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ProviderConfigurationStoreBackend>()
            .FirstOrDefault();
        var entityFrameworkProviderConfigurationAlreadySelected = string.Equals(
            existingProviderConfigurationBackend?.Name,
            "entity-framework",
            StringComparison.Ordinal);
        if (!entityFrameworkProviderConfigurationAlreadySelected)
            ProviderConfigurationStoreBackend.EnsureCompatible(existingProviderConfigurationBackend?.Name, "groundwork");
        if (existingProviderConfigurationBackend is null)
            services.AddSingleton(new ProviderConfigurationStoreBackend("groundwork"));

        services.AddPersistenceCore();
        foreach (var unit in IdentityV2StorageManifest.CreateUnits())
            services.AddGroundworkStorageUnit(unit);
        services.TryAddSingleton<IdentityMutationReceiptCleanupCoordinator>();
        services.TryAddScoped<GroundworkIdentityRowStore>();
        services.TryAddScoped(serviceProvider => new GroundworkIdentityAtomicWrite(
            serviceProvider.GetRequiredService<GroundworkIdentityRowStore>(),
            serviceProvider.GetRequiredService<IdentityMutationReceiptCleanupCoordinator>()));
        services.TryAddScoped<GroundworkIdentityAuthorityRelationshipCoordinator>();
        services.TryAddScoped<GroundworkIdentityAuthorityAggregateCoordinator>();

        services.RemoveAll<IUserStore>();
        services.RemoveAll<IRoleStore>();
        services.RemoveAll<IApplicationStore>();
        services.RemoveAll<ICredentialStore>();
        services.RemoveAll<IClaimMappingStore>();
        if (!entityFrameworkProviderConfigurationAlreadySelected)
            services.RemoveAll<IProviderConfigurationStore>();
        services.RemoveAll<IExternalIdentityStore>();
        services.RemoveAll<ITenantMembershipStore>();

        services.AddScoped<IUserStore, GroundworkUserStore>();
        services.AddScoped<IRoleStore, GroundworkRoleStore>();
        services.AddScoped<IApplicationStore, GroundworkApplicationStore>();
        services.AddScoped<ICredentialStore, GroundworkCredentialStore>();
        services.AddScoped<IClaimMappingStore, GroundworkClaimMappingStore>();
        if (!entityFrameworkProviderConfigurationAlreadySelected)
            services.AddScoped<IProviderConfigurationStore, GroundworkProviderConfigurationStore>();
        services.AddScoped<IExternalIdentityStore, GroundworkExternalIdentityStore>();
        services.AddScoped<ITenantMembershipStore, GroundworkTenantMembershipStore>();

        return services;
    }
}
