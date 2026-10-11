using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Opt-in EF registration for the complete tenant-local Identity IAM store set.</summary>
public static class IdentityIamEntityFrameworkCoreRegistration
{
    public const string StoreBackendName = "entity-framework";
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(IdentityIamDbContext));

    /// <summary>
    /// Executes the complete registration validation against an isolated descriptor list so a
    /// composing adapter can reject an IAM conflict before it selects its own authority.
    /// </summary>
    public static void EnsureCanAddIdentityIamEntityFrameworkCore(
        IServiceCollection services,
        IdentityIamEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var probe = new ServiceCollection();
        foreach (var descriptor in services)
            probe.Add(descriptor);
        probe.AddIdentityIamEntityFrameworkCore(options);
    }

    public static IServiceCollection AddIdentityIamEntityFrameworkCore(
        this IServiceCollection services,
        IdentityIamEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var addContext = Binding.Select<Action<IServiceCollection, IdentityIamEntityFrameworkCoreOptions>>(
            options.Provider,
            AddContext<IdentityIamSqliteDbContext>,
            AddContext<IdentityIamSqlServerDbContext>,
            AddContext<IdentityIamPostgreSqlDbContext>,
            AddContext<IdentityIamMySqlDbContext>);
        var registration = new IdentityIamEfRegistration(provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling);
        var existingRegistration = services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IdentityIamEfRegistration>()
            .SingleOrDefault();
        var existingBackend = services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IdentityApplicationCredentialStoreBackend>()
            .SingleOrDefault();

        if (existingRegistration is not null && existingRegistration != registration)
            throw new InvalidOperationException("Identity IAM EF persistence is already registered with different options.");

        if (existingBackend is not null)
            existingBackend.EnsureOwnsRegisteredContracts(services);
        else if (IdentityApplicationCredentialStoreBackend.HasAnyRegisteredContract(services))
            throw new InvalidOperationException("Identity IAM EF persistence conflicts with an unowned host registration.");

        var existingAuthorityBackend = services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IdentityAuthorityStoreBackend>()
            .SingleOrDefault();
        if (existingAuthorityBackend is not null)
        {
            existingAuthorityBackend.EnsureOwnsRegisteredContracts(services);
        }
        else if (IdentityAuthorityStoreBackend.HasAnyRegisteredContract(services) &&
                 !HasKnownInMemoryAuthorityDescriptors(services))
            throw new InvalidOperationException("Identity IAM EF persistence conflicts with an unowned host registration.");

        if (existingAuthorityBackend is not null &&
            !string.Equals(existingAuthorityBackend.Name, StoreBackendName, StringComparison.Ordinal))
            IdentityAuthorityStoreBackend.EnsureCompatible(existingAuthorityBackend.Name, StoreBackendName);

        if (string.Equals(existingBackend?.Name, StoreBackendName, StringComparison.Ordinal))
        {
            if (existingRegistration is null)
                throw new InvalidOperationException("Identity IAM EF persistence is already registered with different options.");
            return services;
        }

        if (string.Equals(existingAuthorityBackend?.Name, StoreBackendName, StringComparison.Ordinal))
        {
            if (existingRegistration is null)
                throw new InvalidOperationException("Identity IAM EF persistence is already registered with different options.");
            return services;
        }

        if (existingRegistration is not null)
            throw new InvalidOperationException("Identity IAM EF persistence registration is incomplete.");

        if (existingBackend is not null)
            IdentityApplicationCredentialStoreBackend.EnsureCompatible(existingBackend.Name, StoreBackendName);

        services.AddFoundationIdentityAbstractions();
        services.AddPersistenceCore();
        services.RemoveAll<IdentityApplicationCredentialStoreBackend>();
        services.RemoveAll<IdentityAuthorityStoreBackend>();
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
        services.RemoveAll<IApplicationStore>();
        services.RemoveAll<IRevisionAwareApplicationStore>();
        services.RemoveAll<ICredentialStore>();
        services.RemoveAll<IRevisionAwareCredentialStore>();
        services.AddSingleton(registration);
        services.AddSingleton(options);

        addContext(services, options);

        services.AddSingleton<EfIdentityMutationReceiptCleanupCoordinator>();
        services.AddScoped<EfIdentityAtomicWrite>();
        services.AddScoped<EfIdentityAuthorityAggregateCoordinator>();
        services.AddScoped<EfIdentityAuthorityRelationshipCoordinator>();
        services.AddScoped<EfUserStore>();
        services.AddScoped<EfRoleStore>();
        services.AddScoped<EfClaimMappingStore>();
        services.AddScoped<EfExternalIdentityStore>();
        services.AddScoped<EfTenantMembershipStore>();
        services.Add(ServiceDescriptor.Scoped<IUserStore>(provider => provider.GetRequiredService<EfUserStore>()));
        services.Add(ServiceDescriptor.Scoped<IRevisionAwareUserStore>(provider => provider.GetRequiredService<EfUserStore>()));
        services.Add(ServiceDescriptor.Scoped<IRoleStore>(provider => provider.GetRequiredService<EfRoleStore>()));
        services.Add(ServiceDescriptor.Scoped<IRevisionAwareRoleStore>(provider => provider.GetRequiredService<EfRoleStore>()));
        services.Add(ServiceDescriptor.Scoped<IPagedRoleStore>(provider => provider.GetRequiredService<EfRoleStore>()));
        services.Add(ServiceDescriptor.Scoped<IClaimMappingStore>(provider => provider.GetRequiredService<EfClaimMappingStore>()));
        services.Add(ServiceDescriptor.Scoped<IRevisionAwareClaimMappingStore>(provider => provider.GetRequiredService<EfClaimMappingStore>()));
        services.Add(ServiceDescriptor.Scoped<IPagedClaimMappingStore>(provider => provider.GetRequiredService<EfClaimMappingStore>()));
        services.Add(ServiceDescriptor.Scoped<IExternalIdentityStore>(provider => provider.GetRequiredService<EfExternalIdentityStore>()));
        services.Add(ServiceDescriptor.Scoped<IRevisionAwareExternalIdentityStore>(provider => provider.GetRequiredService<EfExternalIdentityStore>()));
        services.Add(ServiceDescriptor.Scoped<IPagedExternalIdentityStore>(provider => provider.GetRequiredService<EfExternalIdentityStore>()));
        services.Add(ServiceDescriptor.Scoped<ITenantMembershipStore>(provider => provider.GetRequiredService<EfTenantMembershipStore>()));
        services.Add(ServiceDescriptor.Scoped<IRevisionAwareTenantMembershipStore>(provider => provider.GetRequiredService<EfTenantMembershipStore>()));
        services.EnsureIdentityAuthorityReplacementContracts<
            EfUserStore,
            EfRoleStore,
            EfClaimMappingStore,
            EfExternalIdentityStore,
            EfTenantMembershipStore>();

        services.AddScoped<EfApplicationStore>();
        services.AddScoped<EfCredentialStore>();
        services.Add(ServiceDescriptor.Scoped<IApplicationStore>(provider => provider.GetRequiredService<EfApplicationStore>()));
        services.Add(ServiceDescriptor.Scoped<IRevisionAwareApplicationStore>(provider => provider.GetRequiredService<EfApplicationStore>()));
        services.Add(ServiceDescriptor.Scoped<ICredentialStore>(provider => provider.GetRequiredService<EfCredentialStore>()));
        services.Add(ServiceDescriptor.Scoped<IRevisionAwareCredentialStore>(provider => provider.GetRequiredService<EfCredentialStore>()));
        services.EnsureReplacementContract<IApplicationStore, EfApplicationStore>();
        services.EnsureReplacementContract<IRevisionAwareApplicationStore, EfApplicationStore>();
        services.EnsureReplacementContract<ICredentialStore, EfCredentialStore>();
        services.EnsureReplacementContract<IRevisionAwareCredentialStore, EfCredentialStore>();
        var authorityBackend = new IdentityAuthorityStoreBackend(StoreBackendName, services);
        services.AddSingleton(authorityBackend);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IValidateOptions<FoundationIdentityOptions>) &&
                                        descriptor.ImplementationInstance is IdentityAuthorityStoreRegistrationValidator))
            services.AddSingleton<IValidateOptions<FoundationIdentityOptions>>(new IdentityAuthorityStoreRegistrationValidator(services, authorityBackend));
        services.AddSingleton(new IdentityApplicationCredentialStoreBackend(StoreBackendName, services));
        return services;
    }

    private static bool HasKnownInMemoryAuthorityDescriptors(IServiceCollection services)
    {
        var descriptors = services.Where(descriptor =>
            descriptor.ServiceType == typeof(IUserStore) ||
            descriptor.ServiceType == typeof(IRoleStore) ||
            descriptor.ServiceType == typeof(IExternalIdentityStore) ||
            descriptor.ServiceType == typeof(ITenantMembershipStore)).ToArray();
        if (descriptors.Length != 4)
            return false;

        var user = descriptors.SingleOrDefault(x => x.ServiceType == typeof(IUserStore));
        if (user is null || user.Lifetime != ServiceLifetime.Singleton ||
            user.ImplementationType?.FullName != "Elsa.Foundation.Identity.AspNetCoreIdentity.Services.InMemoryIdentityStore")
            return false;

        return descriptors
            .Where(x => x.ServiceType != typeof(IUserStore))
            .All(IsKnownInMemoryForwarder);
    }

    private static bool IsKnownInMemoryForwarder(ServiceDescriptor descriptor)
    {
        var declaringType = descriptor.ImplementationFactory?.Method.DeclaringType;
        var ownerName = declaringType?.FullName ?? declaringType?.DeclaringType?.FullName;
        return descriptor.Lifetime == ServiceLifetime.Singleton &&
               descriptor.ImplementationFactory is not null &&
               ownerName?.Contains("Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions.AspNetCoreIdentityServiceCollectionExtensions", StringComparison.Ordinal) == true;
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        IdentityIamEntityFrameworkCoreOptions options)
        where TContext : IdentityIamDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) =>
            Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<IdentityIamDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    private sealed record IdentityIamEfRegistration(string Provider, string? ConnectionString, string? ConnectionName, string? Schema, bool Pooling);
}

public sealed class IdentityIamEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Optional database schema for this module's tables and its own migrations history table. Falls back to
    /// <see cref="EfSchema.ConfigurationKey"/>, then to the provider's own default. Ignored on SQLite and refused
    /// on MySQL, where a schema is a database.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per scope.</summary>
    public bool Pooling { get; set; }
}
