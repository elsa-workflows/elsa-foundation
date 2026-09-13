using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Extensions;
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
        var existingAuthorityBackend = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IdentityAuthorityStoreBackend>()
            .SingleOrDefault();
        existingAuthorityBackend?.EnsureOwnsRegisteredContracts(services);
        var entityFrameworkAuthorityAlreadySelected = string.Equals(
            existingAuthorityBackend?.Name,
            "entity-framework",
            StringComparison.Ordinal);
        var groundworkAuthorityAlreadySelected = string.Equals(
            existingAuthorityBackend?.Name,
            "groundwork",
            StringComparison.Ordinal);
        if (!entityFrameworkAuthorityAlreadySelected)
            IdentityAuthorityStoreBackend.EnsureCompatible(existingAuthorityBackend?.Name, "groundwork");

        var existingIamBackend = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IdentityApplicationCredentialStoreBackend>()
            .SingleOrDefault();
        if (existingIamBackend is not null)
            existingIamBackend.EnsureOwnsRegisteredContracts(services);
        else if (IdentityApplicationCredentialStoreBackend.HasAnyRegisteredContract(services))
            throw new InvalidOperationException("Groundwork Identity application/credential persistence conflicts with an unowned host registration.");
        var entityFrameworkIamAlreadySelected = string.Equals(existingIamBackend?.Name, "entity-framework", StringComparison.Ordinal);
        var groundworkIamAlreadySelected = string.Equals(existingIamBackend?.Name, "groundwork", StringComparison.Ordinal);
        if (!entityFrameworkIamAlreadySelected)
            IdentityApplicationCredentialStoreBackend.EnsureCompatible(existingIamBackend?.Name, "groundwork");

        var existingProviderConfigurationBackend = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ProviderConfigurationStoreBackend>()
            .FirstOrDefault();
        var entityFrameworkProviderConfigurationAlreadySelected = string.Equals(
            existingProviderConfigurationBackend?.Name,
            "entity-framework",
            StringComparison.Ordinal);
        var groundworkProviderConfigurationAlreadySelected = string.Equals(
            existingProviderConfigurationBackend?.Name,
            "groundwork",
            StringComparison.Ordinal);
        if (existingProviderConfigurationBackend is not null)
            existingProviderConfigurationBackend.EnsureOwnsRegisteredContracts(services);
        else if (services.Any(descriptor => descriptor.ServiceType is not null &&
                                            (descriptor.ServiceType == typeof(IProviderConfigurationStore) ||
                                             descriptor.ServiceType == typeof(IRevisionAwareProviderConfigurationStore))))
            throw new InvalidOperationException("Groundwork provider-configuration persistence conflicts with an unowned host registration.");
        if (!entityFrameworkProviderConfigurationAlreadySelected)
            ProviderConfigurationStoreBackend.EnsureCompatible(existingProviderConfigurationBackend?.Name, "groundwork");

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

        if (!entityFrameworkAuthorityAlreadySelected && !groundworkAuthorityAlreadySelected)
        {
            services.RemoveAll<IUserStore>();
            services.RemoveAll<IRevisionAwareUserStore>();
            services.RemoveAll<IRoleStore>();
            services.RemoveAll<IRevisionAwareRoleStore>();
            services.RemoveAll<IPagedRoleStore>();
            services.RemoveAll<IClaimMappingStore>();
            services.RemoveAll<IRevisionAwareClaimMappingStore>();
            services.RemoveAll<IPagedClaimMappingStore>();
            services.RemoveAll<IExternalIdentityStore>();
            services.RemoveAll<IRevisionAwareExternalIdentityStore>();
            services.RemoveAll<IPagedExternalIdentityStore>();
            services.RemoveAll<ITenantMembershipStore>();
            services.RemoveAll<IRevisionAwareTenantMembershipStore>();
        }
        if (!entityFrameworkIamAlreadySelected && !groundworkIamAlreadySelected)
        {
            services.RemoveAll<IApplicationStore>();
            services.RemoveAll<IRevisionAwareApplicationStore>();
            services.RemoveAll<ICredentialStore>();
            services.RemoveAll<IRevisionAwareCredentialStore>();
        }
        if (!entityFrameworkProviderConfigurationAlreadySelected && !groundworkProviderConfigurationAlreadySelected)
        {
            services.RemoveAll<IProviderConfigurationStore>();
            services.RemoveAll<IRevisionAwareProviderConfigurationStore>();
        }
        if (!entityFrameworkAuthorityAlreadySelected && !groundworkAuthorityAlreadySelected)
        {
            services.AddScoped<IUserStore, GroundworkUserStore>();
            services.Add(ServiceDescriptor.Scoped<IRevisionAwareUserStore>(provider =>
                (IRevisionAwareUserStore)provider.GetRequiredService<IUserStore>()));
            services.AddScoped<IRoleStore, GroundworkRoleStore>();
            services.Add(ServiceDescriptor.Scoped<IRevisionAwareRoleStore>(provider =>
                (IRevisionAwareRoleStore)provider.GetRequiredService<IRoleStore>()));
            services.Add(ServiceDescriptor.Scoped<IPagedRoleStore>(provider =>
                (IPagedRoleStore)provider.GetRequiredService<IRoleStore>()));
            services.AddScoped<IClaimMappingStore, GroundworkClaimMappingStore>();
            services.Add(ServiceDescriptor.Scoped<IRevisionAwareClaimMappingStore>(provider =>
                (IRevisionAwareClaimMappingStore)provider.GetRequiredService<IClaimMappingStore>()));
            services.Add(ServiceDescriptor.Scoped<IPagedClaimMappingStore>(provider =>
                (IPagedClaimMappingStore)provider.GetRequiredService<IClaimMappingStore>()));
            services.AddScoped<IExternalIdentityStore, GroundworkExternalIdentityStore>();
            services.Add(ServiceDescriptor.Scoped<IRevisionAwareExternalIdentityStore>(provider =>
                (IRevisionAwareExternalIdentityStore)provider.GetRequiredService<IExternalIdentityStore>()));
            services.Add(ServiceDescriptor.Scoped<IPagedExternalIdentityStore>(provider =>
                (IPagedExternalIdentityStore)provider.GetRequiredService<IExternalIdentityStore>()));
            services.AddScoped<ITenantMembershipStore, GroundworkTenantMembershipStore>();
            services.Add(ServiceDescriptor.Scoped<IRevisionAwareTenantMembershipStore>(provider =>
                (IRevisionAwareTenantMembershipStore)provider.GetRequiredService<ITenantMembershipStore>()));
            services.EnsureIdentityAuthorityReplacementContracts<
                GroundworkUserStore,
                GroundworkRoleStore,
                GroundworkClaimMappingStore,
                GroundworkExternalIdentityStore,
                GroundworkTenantMembershipStore>();
            services.AddSingleton(new IdentityAuthorityStoreBackend("groundwork", services));
        }
        if (!entityFrameworkIamAlreadySelected && !groundworkIamAlreadySelected)
        {
            services.AddFoundationIdentityAbstractions();
            services.TryAddScoped<GroundworkApplicationStore>();
            services.TryAddScoped<GroundworkCredentialStore>();
            services.Add(ServiceDescriptor.Scoped<IApplicationStore>(provider =>
                provider.GetRequiredService<GroundworkApplicationStore>()));
            services.Add(ServiceDescriptor.Scoped<IRevisionAwareApplicationStore>(provider =>
                provider.GetRequiredService<GroundworkApplicationStore>()));
            services.Add(ServiceDescriptor.Scoped<ICredentialStore>(provider =>
                provider.GetRequiredService<GroundworkCredentialStore>()));
            services.Add(ServiceDescriptor.Scoped<IRevisionAwareCredentialStore>(provider =>
                provider.GetRequiredService<GroundworkCredentialStore>()));
            services.EnsureReplacementContract<IApplicationStore, GroundworkApplicationStore>();
            services.EnsureReplacementContract<IRevisionAwareApplicationStore, GroundworkApplicationStore>();
            services.EnsureReplacementContract<ICredentialStore, GroundworkCredentialStore>();
            services.EnsureReplacementContract<IRevisionAwareCredentialStore, GroundworkCredentialStore>();
            services.AddSingleton(new IdentityApplicationCredentialStoreBackend("groundwork", services));
        }
        if (!entityFrameworkProviderConfigurationAlreadySelected && !groundworkProviderConfigurationAlreadySelected)
        {
            services.AddFoundationIdentityAbstractions();
            services.TryAddScoped<GroundworkProviderConfigurationStore>();
            var providerDescriptor = ServiceDescriptor.Scoped<IProviderConfigurationStore>(provider =>
                provider.GetRequiredService<GroundworkProviderConfigurationStore>());
            var revisionDescriptor = ServiceDescriptor.Scoped<IRevisionAwareProviderConfigurationStore>(provider =>
                provider.GetRequiredService<GroundworkProviderConfigurationStore>());
            services.Add(providerDescriptor);
            services.Add(revisionDescriptor);
            services.EnsureReplacementContract<IProviderConfigurationStore, GroundworkProviderConfigurationStore>();
            services.EnsureReplacementContract<IRevisionAwareProviderConfigurationStore, GroundworkProviderConfigurationStore>();
            services.AddSingleton(new ProviderConfigurationStoreBackend("groundwork", providerDescriptor, revisionDescriptor));
        }
        return services;
    }
}
