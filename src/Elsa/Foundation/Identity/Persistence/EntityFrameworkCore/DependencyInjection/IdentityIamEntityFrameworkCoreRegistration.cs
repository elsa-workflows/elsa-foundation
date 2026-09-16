using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Opt-in EF registration for the complete tenant-local Identity IAM store set.</summary>
public static class IdentityIamEntityFrameworkCoreRegistration
{
    public const string StoreBackendName = "entity-framework";

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
        EnsureSupportedProvider(provider, options.Provider);
        var registration = new IdentityIamEfRegistration(provider, options.ConnectionString, options.ConnectionName);
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

        switch (provider)
        {
            case "sqlite":
                AddContext<IdentityIamSqliteDbContext>(services, options, EfRelationalProviderBinding.UseSqlite);
                break;
            case "sqlserver":
                AddContext<IdentityIamSqlServerDbContext>(services, options, EfRelationalProviderBinding.UseSqlServer);
                break;
            case "postgresql":
                AddContext<IdentityIamPostgreSqlDbContext>(services, options, EfRelationalProviderBinding.UseNpgsql);
                break;
            case "mysql":
                AddContext<IdentityIamMySqlDbContext>(services, options, EfRelationalProviderBinding.UseMySql);
                break;
        }

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

    private static void EnsureSupportedProvider(string provider, string configuredProvider)
    {
        if (provider is not ("sqlite" or "sqlserver" or "postgresql" or "mysql"))
            throw new ArgumentException(
                $"Unknown Identity IAM EF provider '{configuredProvider}'. Expected Sqlite, SqlServer, PostgreSql, or MySql.",
                nameof(configuredProvider));
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        IdentityIamEntityFrameworkCoreOptions options,
        Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : IdentityIamDbContext
    {
        services.AddDbContext<TContext>((provider, builder) =>
            bind(
                builder,
                ResolveConnectionString(provider, options),
                IdentityIamEfModule.HistoryTableName,
                typeof(IdentityIamDbContext).Assembly.GetName().Name));
        services.AddScoped<IdentityIamDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    internal static string ResolveConnectionString(IServiceProvider provider, IdentityIamEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;

        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
        {
            var named = configuration?.GetConnectionString(options.ConnectionName);
            if (string.IsNullOrWhiteSpace(named))
                throw new InvalidOperationException($"Identity IAM EF connection '{options.ConnectionName}' was not found in ConnectionStrings.");
            return named;
        }

        var fallback = configuration?.GetConnectionString(IdentityIamEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return IdentityIamEfModule.DefaultSqliteConnectionString;

        throw new InvalidOperationException("Identity IAM EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }

    private sealed record IdentityIamEfRegistration(string Provider, string? ConnectionString, string? ConnectionName);
}

public sealed class IdentityIamEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
