using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

public sealed class IdentityIamEntityFrameworkCoreRegistrationTests
{
    private static readonly Type[] ApplicationCredentialContracts =
    [
        typeof(IApplicationStore),
        typeof(IRevisionAwareApplicationStore),
        typeof(ICredentialStore),
        typeof(IRevisionAwareCredentialStore)
    ];

    private static readonly Type[] AuthorityContracts =
    [
        typeof(IUserStore),
        typeof(IRevisionAwareUserStore),
        typeof(IRoleStore),
        typeof(IRevisionAwareRoleStore),
        typeof(IPagedRoleStore),
        typeof(IClaimMappingStore),
        typeof(IRevisionAwareClaimMappingStore),
        typeof(IPagedClaimMappingStore),
        typeof(IExternalIdentityStore),
        typeof(IRevisionAwareExternalIdentityStore),
        typeof(IPagedExternalIdentityStore),
        typeof(ITenantMembershipStore),
        typeof(IRevisionAwareTenantMembershipStore)
    ];

    [Fact]
    public void Registration_replaces_exactly_the_seventeen_IAM_contracts()
    {
        var services = new ServiceCollection();

        services.AddIdentityIamEntityFrameworkCore(SqliteOptions());

        var replacementContracts = ApplicationCredentialContracts.Concat(AuthorityContracts).ToArray();

        Assert.Equal(17, services.Count(descriptor => replacementContracts.Contains(descriptor.ServiceType)));
        Assert.All(replacementContracts, contract =>
        {
            var descriptor = Assert.Single(services, candidate => candidate.ServiceType == contract);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            Assert.NotNull(descriptor.ImplementationFactory);
        });
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfApplicationStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfCredentialStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfUserStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfRoleStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfClaimMappingStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfExternalIdentityStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfTenantMembershipStore));
        Assert.Equal(IdentityIamEntityFrameworkCoreRegistration.StoreBackendName,
            Assert.Single(services
                .Where(descriptor => descriptor.ServiceType == typeof(IdentityAuthorityStoreBackend))
                .Select(descriptor => Assert.IsType<IdentityAuthorityStoreBackend>(descriptor.ImplementationInstance)))
                .Name);
        Assert.Equal(
            IdentityIamEntityFrameworkCoreRegistration.StoreBackendName,
            Assert.Single(services
                .Where(descriptor => descriptor.ServiceType == typeof(IdentityApplicationCredentialStoreBackend))
                .Select(descriptor => Assert.IsType<IdentityApplicationCredentialStoreBackend>(descriptor.ImplementationInstance)))
                .Name);
    }

    [Fact]
    public async Task Registration_resolves_all_seventeen_contracts_to_one_scoped_EF_store_each()
    {
        var services = new ServiceCollection();
        services.AddIdentityIamEntityFrameworkCore(SqliteOptions());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IStartupValidator>().Validate();

        await using var firstScope = provider.CreateAsyncScope();
        var application = firstScope.ServiceProvider.GetRequiredService<IApplicationStore>();
        var revisionAwareApplication = firstScope.ServiceProvider.GetRequiredService<IRevisionAwareApplicationStore>();
        var credential = firstScope.ServiceProvider.GetRequiredService<ICredentialStore>();
        var revisionAwareCredential = firstScope.ServiceProvider.GetRequiredService<IRevisionAwareCredentialStore>();

        Assert.IsType<EfApplicationStore>(application);
        Assert.Same(application, revisionAwareApplication);
        Assert.IsType<EfCredentialStore>(credential);
        Assert.Same(credential, revisionAwareCredential);
        Assert.NotSame(application, credential);

        var user = firstScope.ServiceProvider.GetRequiredService<IUserStore>();
        Assert.IsType<EfUserStore>(user);
        Assert.Same(user, firstScope.ServiceProvider.GetRequiredService<IRevisionAwareUserStore>());
        var role = firstScope.ServiceProvider.GetRequiredService<IRoleStore>();
        Assert.IsType<EfRoleStore>(role);
        Assert.Same(role, firstScope.ServiceProvider.GetRequiredService<IRevisionAwareRoleStore>());
        Assert.Same(role, firstScope.ServiceProvider.GetRequiredService<IPagedRoleStore>());
        var claimMapping = firstScope.ServiceProvider.GetRequiredService<IClaimMappingStore>();
        Assert.IsType<EfClaimMappingStore>(claimMapping);
        Assert.Same(claimMapping, firstScope.ServiceProvider.GetRequiredService<IRevisionAwareClaimMappingStore>());
        Assert.Same(claimMapping, firstScope.ServiceProvider.GetRequiredService<IPagedClaimMappingStore>());
        var externalIdentity = firstScope.ServiceProvider.GetRequiredService<IExternalIdentityStore>();
        Assert.IsType<EfExternalIdentityStore>(externalIdentity);
        Assert.Same(externalIdentity, firstScope.ServiceProvider.GetRequiredService<IRevisionAwareExternalIdentityStore>());
        Assert.Same(externalIdentity, firstScope.ServiceProvider.GetRequiredService<IPagedExternalIdentityStore>());
        var membership = firstScope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
        Assert.IsType<EfTenantMembershipStore>(membership);
        Assert.Same(membership, firstScope.ServiceProvider.GetRequiredService<IRevisionAwareTenantMembershipStore>());

        await using var secondScope = provider.CreateAsyncScope();
        Assert.NotSame(application, secondScope.ServiceProvider.GetRequiredService<IApplicationStore>());
        Assert.NotSame(credential, secondScope.ServiceProvider.GetRequiredService<ICredentialStore>());
        Assert.NotSame(user, secondScope.ServiceProvider.GetRequiredService<IUserStore>());
        Assert.NotSame(role, secondScope.ServiceProvider.GetRequiredService<IRoleStore>());
    }

    [Fact]
    public async Task Groundwork_only_exposes_both_revision_aware_aliases_to_one_concrete_store_each()
    {
        var services = GroundworkServices();
        services.AddGroundworkIdentityStores();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IStartupValidator>().Validate();
        await using var scope = provider.CreateAsyncScope();

        var application = scope.ServiceProvider.GetRequiredService<IApplicationStore>();
        var revisionAwareApplication = scope.ServiceProvider.GetRequiredService<IRevisionAwareApplicationStore>();
        var credential = scope.ServiceProvider.GetRequiredService<ICredentialStore>();
        var revisionAwareCredential = scope.ServiceProvider.GetRequiredService<IRevisionAwareCredentialStore>();

        Assert.IsType<GroundworkApplicationStore>(application);
        Assert.Same(application, revisionAwareApplication);
        Assert.IsType<GroundworkCredentialStore>(credential);
        Assert.Same(credential, revisionAwareCredential);
        AssertGroundworkAuthorityStores(services);
    }

    [Fact]
    public void Registration_replaces_all_Groundwork_IAM_authority_stores_and_preserves_provider_configuration()
    {
        var services = GroundworkServices();
        services.AddGroundworkIdentityStores();

        var groundworkProvider = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore));
        var groundworkRevisionProvider = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRevisionAwareProviderConfigurationStore));

        services.AddIdentityIamEntityFrameworkCore(SqliteOptions());

        Assert.Same(groundworkProvider, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore)));
        Assert.Same(groundworkRevisionProvider, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRevisionAwareProviderConfigurationStore)));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(GroundworkProviderConfigurationStore));
        AssertIamAuthorityBackend(services);
        AssertIamApplicationCredentialBackend(services);
    }

    [Fact]
    public void Registration_is_stable_when_EF_or_Groundwork_is_registered_first()
    {
        var groundworkFirst = GroundworkServices();
        groundworkFirst.AddGroundworkIdentityStores();
        groundworkFirst.AddIdentityIamEntityFrameworkCore(SqliteOptions());

        var efFirst = GroundworkServices();
        efFirst.AddIdentityIamEntityFrameworkCore(SqliteOptions());
        efFirst.AddGroundworkIdentityStores();

        AssertIamBackend(groundworkFirst, typeof(EfApplicationStore), typeof(EfCredentialStore));
        AssertIamBackend(efFirst, typeof(EfApplicationStore), typeof(EfCredentialStore));
        AssertIamAuthorityBackend(groundworkFirst);
        AssertIamAuthorityBackend(efFirst);
        AssertProviderConfigurationIsGroundwork(groundworkFirst);
        AssertProviderConfigurationIsGroundwork(efFirst);

        using (var groundworkFirstProvider = groundworkFirst.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
            groundworkFirstProvider.GetRequiredService<IStartupValidator>().Validate();
        using var efFirstProvider = efFirst.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        efFirstProvider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void Registration_accepts_the_known_in_memory_authority_factory_shape()
    {
        var services = new ServiceCollection();
        services.AddInMemoryAuthorityShape();

        services.AddIdentityIamEntityFrameworkCore(SqliteOptions());

        AssertIamAuthorityBackend(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void Equivalent_registration_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddIdentityIamEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        var before = services.ToArray();

        services.AddIdentityIamEntityFrameworkCore(new()
        {
            Provider = "sqlite",
            ConnectionString = "Data Source=:memory:"
        });

        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Invalid_or_conflicting_registration_does_not_partially_mutate_services()
    {
        var invalid = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => invalid.AddIdentityIamEntityFrameworkCore(new() { Provider = "unknown" }));
        Assert.Empty(invalid);

        var services = new ServiceCollection();
        services.AddIdentityIamEntityFrameworkCore(SqliteOptions());
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddIdentityIamEntityFrameworkCore(new()
        {
            Provider = "SqlServer",
            ConnectionString = "Server=localhost"
        }));
        Assert.Equal(before, services.ToArray());

        Assert.Throws<InvalidOperationException>(() => services.AddIdentityIamEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=other.db"
        }));
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Unowned_application_and_credential_registrations_before_EF_are_rejected_without_mutation()
    {
        var services = new ServiceCollection();
        AddUnownedApplicationAndCredentialStores(services);
        var before = services.ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddIdentityIamEntityFrameworkCore(SqliteOptions()));

        Assert.Contains("unowned host registration", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Unowned_application_and_credential_registrations_after_Groundwork_are_rejected_without_mutation()
    {
        var services = GroundworkServices();
        services.AddGroundworkIdentityStores();
        AddUnownedApplicationAndCredentialStores(services);
        var before = services.ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddIdentityIamEntityFrameworkCore(SqliteOptions()));

        Assert.Contains("no longer exclusively owns", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Unowned_application_and_credential_registrations_after_EF_fail_startup_validation()
    {
        var services = new ServiceCollection();
        services.AddIdentityIamEntityFrameworkCore(SqliteOptions());
        AddUnownedApplicationAndCredentialStores(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(typeof(IApplicationStore).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(IRevisionAwareApplicationStore).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(ICredentialStore).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(IRevisionAwareCredentialStore).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unowned_authority_registration_before_EF_is_rejected_without_mutation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserStore, UnownedAuthorityStore>();
        var before = services.ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddIdentityIamEntityFrameworkCore(SqliteOptions()));

        Assert.Contains("unowned host registration", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Custom_authority_backend_before_EF_is_rejected_without_mutation()
    {
        var services = new ServiceCollection();
        foreach (var contract in AuthorityContracts)
        {
            ((IServiceCollection)services).Add(ServiceDescriptor.Describe(
                contract,
                static _ => new object(),
                ServiceLifetime.Scoped));
        }

        services.AddSingleton(new IdentityAuthorityStoreBackend("custom", services));
        var before = services.ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddIdentityIamEntityFrameworkCore(SqliteOptions()));

        Assert.Contains("already bound to 'custom'", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Unowned_authority_registration_after_Groundwork_is_rejected_without_mutation()
    {
        var services = GroundworkServices();
        services.AddGroundworkIdentityStores();
        services.AddScoped<IUserStore, UnownedAuthorityStore>();
        var before = services.ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddIdentityIamEntityFrameworkCore(SqliteOptions()));

        Assert.Contains("no longer exclusively owns", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Unowned_authority_registration_after_EF_fails_startup_validation()
    {
        var services = new ServiceCollection();
        services.AddIdentityIamEntityFrameworkCore(SqliteOptions());
        services.AddScoped<IUserStore, UnownedAuthorityStore>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(typeof(IUserStore).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sqlite", typeof(IdentityIamSqliteDbContext))]
    [InlineData("SqlServer", typeof(IdentityIamSqlServerDbContext))]
    [InlineData("PostgreSql", typeof(IdentityIamPostgreSqlDbContext))]
    [InlineData("MySql", typeof(IdentityIamMySqlDbContext))]
    public void Provider_selection_registers_only_the_selected_IAM_context(string provider, Type expectedContext)
    {
        var services = new ServiceCollection();
        services.AddIdentityIamEntityFrameworkCore(new()
        {
            Provider = provider,
            ConnectionString = "Data Source=:memory:"
        });

        var selectedContexts = services
            .Where(descriptor =>
                typeof(IdentityIamDbContext).IsAssignableFrom(descriptor.ServiceType) &&
                descriptor.ServiceType != typeof(IdentityIamDbContext))
            .Select(descriptor => descriptor.ServiceType)
            .ToArray();

        Assert.Equal([expectedContext], selectedContexts);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IdentityIamDbContext));
    }

    [Fact]
    public void Connection_resolution_prefers_explicit_then_named_then_default_and_uses_SQLite_fallback()
    {
        Assert.Equal(
            "Data Source=explicit.db",
            ResolveSqliteConnection(new()
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=explicit.db",
                ConnectionName = "Named"
            },
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Named"] = "Data Source=named.db",
                [$"ConnectionStrings:{IdentityIamEfModule.DefaultConnectionName}"] = "Data Source=default.db"
            }));

        Assert.Equal(
            "Data Source=named.db",
            ResolveSqliteConnection(new() { Provider = "Sqlite", ConnectionName = "Named" },
                new Dictionary<string, string?> { ["ConnectionStrings:Named"] = "Data Source=named.db" }));

        Assert.Equal(
            "Data Source=default.db",
            ResolveSqliteConnection(new() { Provider = "Sqlite" },
                new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{IdentityIamEfModule.DefaultConnectionName}"] = "Data Source=default.db"
                }));

        Assert.Equal(
            IdentityIamEfModule.DefaultSqliteConnectionString,
            ResolveSqliteConnection(new() { Provider = "Sqlite" }));
    }

    [Fact]
    public void Blank_explicit_connection_falls_through_to_named_connection()
    {
        Assert.Equal(
            "Data Source=named.db",
            ResolveSqliteConnection(new()
            {
                Provider = "Sqlite",
                ConnectionString = "  ",
                ConnectionName = "Named"
            },
            new Dictionary<string, string?> { ["ConnectionStrings:Named"] = "Data Source=named.db" }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Missing_or_blank_named_connection_fails(string? configuredConnection)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Missing"] = configuredConnection
            })
            .Build());
        services.AddIdentityIamEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionName = "Missing" });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>());

        Assert.Contains("Missing", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    public void Non_SQLite_connection_resolution_requires_a_connection(string providerName)
    {
        var services = new ServiceCollection();
        services.AddIdentityIamEntityFrameworkCore(new() { Provider = providerName });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>());

        Assert.Contains("requires ConnectionString or ConnectionName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IAM_EF_feature_validates_at_startup_and_does_not_merge_OpenIddict()
    {
        var services = new ServiceCollection();
        new IdentityIamEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IStartupValidator>().Validate();
        using var scope = provider.CreateScope();
        var context = Assert.IsType<IdentityIamSqliteDbContext>(
            scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>());
        var applicationStore = scope.ServiceProvider.GetRequiredService<EfApplicationStore>();
        var credentialStore = scope.ServiceProvider.GetRequiredService<EfCredentialStore>();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());
        Assert.Same(applicationStore, scope.ServiceProvider.GetRequiredService<IApplicationStore>());
        Assert.Same(applicationStore, scope.ServiceProvider.GetRequiredService<IRevisionAwareApplicationStore>());
        Assert.Same(credentialStore, scope.ServiceProvider.GetRequiredService<ICredentialStore>());
        Assert.Same(credentialStore, scope.ServiceProvider.GetRequiredService<IRevisionAwareCredentialStore>());
        Assert.Equal(
            IdentityIamEfModule.HistoryTableName,
            context.GetService<IDbContextOptions>().Extensions
                .OfType<RelationalOptionsExtension>()
                .Single()
                .MigrationsHistoryTableName);
        Assert.DoesNotContain(context.Model.GetEntityTypes(), entity =>
            entity.ClrType.FullName?.Contains("OpenIddict", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(services, descriptor =>
            string.Concat(
                    descriptor.ServiceType.FullName,
                    descriptor.ImplementationType?.FullName,
                    descriptor.ImplementationInstance?.GetType().FullName)
                .Contains("OpenIddict", StringComparison.Ordinal));
    }

    private static IdentityIamEntityFrameworkCoreOptions SqliteOptions() => new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=:memory:"
    };

    private static IServiceCollection GroundworkServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGroundworkStorageSessionSource, UnusedGroundworkStorageSessionSource>();
        return services;
    }

    private static void AssertIamBackend(
        IServiceCollection services,
        Type expectedApplicationStore,
        Type expectedCredentialStore)
    {
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IApplicationStore) &&
            descriptor.ImplementationFactory is not null);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IRevisionAwareApplicationStore) &&
            descriptor.ImplementationFactory is not null);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ICredentialStore) &&
            descriptor.ImplementationFactory is not null);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IRevisionAwareCredentialStore) &&
            descriptor.ImplementationFactory is not null);
        Assert.Contains(services, descriptor => descriptor.ServiceType == expectedApplicationStore);
        Assert.Contains(services, descriptor => descriptor.ServiceType == expectedCredentialStore);
        Assert.Equal(
            IdentityIamEntityFrameworkCoreRegistration.StoreBackendName,
            Assert.Single(services
                .Where(descriptor => descriptor.ServiceType == typeof(IdentityApplicationCredentialStoreBackend))
                .Select(descriptor => Assert.IsType<IdentityApplicationCredentialStoreBackend>(descriptor.ImplementationInstance)))
                .Name);
    }

    private static void AssertIamApplicationCredentialBackend(IServiceCollection services)
    {
        foreach (var contract in ApplicationCredentialContracts)
        {
            var descriptor = Assert.Single(services, candidate => candidate.ServiceType == contract);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            Assert.NotNull(descriptor.ImplementationFactory);
        }

        Assert.Equal(
            IdentityIamEntityFrameworkCoreRegistration.StoreBackendName,
            Assert.Single(services
                .Where(descriptor => descriptor.ServiceType == typeof(IdentityApplicationCredentialStoreBackend))
                .Select(descriptor => Assert.IsType<IdentityApplicationCredentialStoreBackend>(descriptor.ImplementationInstance)))
                .Name);
    }

    private static void AssertIamAuthorityBackend(IServiceCollection services)
    {
        foreach (var contract in AuthorityContracts)
        {
            var descriptor = Assert.Single(services, candidate => candidate.ServiceType == contract);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            Assert.NotNull(descriptor.ImplementationFactory);
        }

        Assert.Equal(
            IdentityIamEntityFrameworkCoreRegistration.StoreBackendName,
            Assert.Single(services
                .Where(descriptor => descriptor.ServiceType == typeof(IdentityAuthorityStoreBackend))
                .Select(descriptor => Assert.IsType<IdentityAuthorityStoreBackend>(descriptor.ImplementationInstance)))
                .Name);
    }

    private static void AssertGroundworkUnrelatedStores(IServiceCollection services)
    {
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IUserStore) && descriptor.ImplementationType == typeof(GroundworkUserStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRoleStore) && descriptor.ImplementationType == typeof(GroundworkRoleStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IClaimMappingStore) && descriptor.ImplementationType == typeof(GroundworkClaimMappingStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExternalIdentityStore) && descriptor.ImplementationType == typeof(GroundworkExternalIdentityStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ITenantMembershipStore) && descriptor.ImplementationType == typeof(GroundworkTenantMembershipStore));
    }

    private static void AssertGroundworkAuthorityStores(IServiceCollection services)
    {
        AssertGroundworkUnrelatedStores(services);
        foreach (var contract in AuthorityContracts)
        {
            var descriptor = Assert.Single(services, candidate => candidate.ServiceType == contract);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        }
    }

    private static void AssertProviderConfigurationIsGroundwork(IServiceCollection services)
    {
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore) && descriptor.ImplementationFactory is not null);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRevisionAwareProviderConfigurationStore) && descriptor.ImplementationFactory is not null);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(GroundworkProviderConfigurationStore));
        Assert.Equal(
            "groundwork",
            Assert.Single(services
                .Where(descriptor => descriptor.ServiceType == typeof(ProviderConfigurationStoreBackend))
                .Select(descriptor => Assert.IsType<ProviderConfigurationStoreBackend>(descriptor.ImplementationInstance)))
                .Name);
    }

    private static void AddUnownedApplicationAndCredentialStores(IServiceCollection services)
    {
        services.AddScoped<IApplicationStore, UnownedApplicationCredentialStore>();
        services.AddScoped<IRevisionAwareApplicationStore, UnownedApplicationCredentialStore>();
        services.AddScoped<ICredentialStore, UnownedApplicationCredentialStore>();
        services.AddScoped<IRevisionAwareCredentialStore, UnownedApplicationCredentialStore>();
    }

    private static string ResolveSqliteConnection(
        IdentityIamEntityFrameworkCoreOptions options,
        IReadOnlyDictionary<string, string?>? configurationValues = null)
    {
        var services = new ServiceCollection();
        if (configurationValues is not null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configurationValues)
                .Build();
            services.AddSingleton<IConfiguration>(configuration);
        }

        services.AddIdentityIamEntityFrameworkCore(options);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>();
        return Assert.IsType<string>(context.Database.GetConnectionString());
    }

    private sealed class UnownedApplicationCredentialStore :
        IApplicationStore,
        IRevisionAwareApplicationStore,
        ICredentialStore,
        IRevisionAwareCredentialStore
    {
        ValueTask<ApplicationRecord?> IApplicationStore.FindAsync(string tenantId, string applicationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        ValueTask IApplicationStore.SaveAsync(ApplicationRecord application, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        ValueTask<IamRevisionedRecord<ApplicationRecord>?> IRevisionAwareApplicationStore.FindWithRevisionAsync(string tenantId, string applicationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        ValueTask<IamRevisionSaveResult> IRevisionAwareApplicationStore.SaveWithRevisionAsync(ApplicationRecord application, string? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        ValueTask<CredentialRecord?> ICredentialStore.FindAsync(string tenantId, string credentialId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        ValueTask ICredentialStore.SaveAsync(CredentialRecord credential, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        ValueTask<IamRevisionedRecord<CredentialRecord>?> IRevisionAwareCredentialStore.FindWithRevisionAsync(string tenantId, string credentialId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        ValueTask<IamRevisionSaveResult> IRevisionAwareCredentialStore.SaveWithRevisionAsync(CredentialRecord credential, string? expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnownedAuthorityStore : IUserStore
    {
        public ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedGroundworkStorageSessionSource : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            throw new NotSupportedException();

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) =>
            throw new NotSupportedException();
    }
}
