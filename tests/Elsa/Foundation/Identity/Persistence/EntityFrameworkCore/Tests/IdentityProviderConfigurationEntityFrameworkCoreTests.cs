using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

public sealed class IdentityProviderConfigurationEntityFrameworkCoreTests
{
    [Fact]
    public async Task Tenant_and_global_records_round_trip_with_canonical_lookup_and_gw_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = Configuration("acme", "Entra", "tenant");
        var global = Configuration(null, "Entra", "global");

        await fixture.TenantStore.SaveAsync(tenant);
        await fixture.GlobalStore.SaveAsync(global);

        var tenantRead = await fixture.TenantStore.FindForTenantAsync("acme", "entra");
        var globalRead = await fixture.GlobalStore.FindGlobalAsync("ENTRA");
        Assert.NotNull(tenantRead);
        Assert.NotNull(globalRead);
        Assert.Equal(tenant, tenantRead with { Settings = tenant.Settings });
        Assert.Equal(global.Provider, globalRead!.Provider);
        Assert.Equal(global.TenantId, globalRead.TenantId);
        Assert.Equal(global.Kind, globalRead.Kind);
        Assert.Equal(global.Settings, globalRead.Settings);
        var revisioned = await fixture.TenantRevisionStore.FindForTenantWithRevisionAsync("acme", "Entra");
        Assert.NotNull(revisioned);
        Assert.StartsWith("gw:", revisioned!.Revision);
        Assert.Equal(23, revisioned.Revision.Length);
    }

    [Fact]
    public async Task Cas_is_create_only_then_rotates_and_rejects_stale_without_mutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = Configuration("acme", "oidc", "first");
        var second = first with { Kind = "second" };

        var created = await fixture.TenantRevisionStore.SaveWithRevisionAsync(first, expectedRevision: null);
        Assert.Equal(IamRevisionSaveStatus.Saved, created.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, (await fixture.TenantRevisionStore.SaveWithRevisionAsync(first, "gw:00000000000000000000")).Status);
        Assert.Equal(IamRevisionSaveStatus.NotFound, (await fixture.TenantRevisionStore.SaveWithRevisionAsync(Configuration("acme", "missing", "kind"), created.Revision)).Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, (await fixture.TenantRevisionStore.SaveWithRevisionAsync(second, null)).Status);
        var updated = await fixture.TenantRevisionStore.SaveWithRevisionAsync(second, created.Revision);
        Assert.Equal(IamRevisionSaveStatus.Saved, updated.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, (await fixture.TenantRevisionStore.SaveWithRevisionAsync(first, created.Revision)).Status);
        Assert.Equal("second", (await fixture.TenantStore.FindForTenantAsync("acme", "oidc"))!.Kind);
    }

    [Fact]
    public async Task Access_guard_rejects_cross_tenant_and_unprivileged_global_access()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.TenantStore.FindForTenantAsync("other", "oidc").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.TenantRevisionStore.FindForTenantWithRevisionAsync("other", "oidc").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.TenantStore.FindGlobalWithRevisionAsync("oidc").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UnprivilegedGlobalStore.SaveAsync(Configuration(null, "oidc", "global")).AsTask());
    }

    [Fact]
    public async Task Revision_reads_reject_a_mismatched_provider_before_database_access()
    {
        var options = new DbContextOptionsBuilder<IdentityProviderConfigurationSqlServerDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var context = new IdentityProviderConfigurationSqlServerDbContext(options);
        var globalStore = new EfProviderConfigurationStore(context,
            new FakeAccessAccessor(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("test"))));
        var tenantStore = new EfProviderConfigurationStore(context,
            new FakeAccessAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("acme"))));

        var globalException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => globalStore.FindGlobalWithRevisionAsync("oidc").AsTask());
        var tenantException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tenantStore.FindForTenantWithRevisionAsync("acme", "oidc").AsTask());

        Assert.Contains(IdentityProviderConfigurationSqlServerDbContext.ExpectedProviderName, globalException.Message, StringComparison.Ordinal);
        Assert.Contains(IdentityProviderConfigurationSqlServerDbContext.ExpectedProviderName, tenantException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Effective_fallback_does_not_bypass_explicit_global_access()
    {
        await using var fixture = await Fixture.CreateAsync();
        IProviderConfigurationStore store = fixture.TenantStore;
        var global = Configuration(null, "fallback", "global");
        await fixture.GlobalStore.SaveAsync(global);
        Assert.Null(await store.FindEffectiveAsync("acme", "missing", allowGlobalFallback: false));
        Assert.Equal(global.Kind, (await store.FindEffectiveAsync("acme", "fallback", allowGlobalFallback: true))!.Kind);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindGlobalAsync("fallback").AsTask());
        var tenant = Configuration("acme", "fallback", "tenant");
        await fixture.TenantStore.SaveAsync(tenant);
        Assert.Equal(tenant.Kind, (await store.FindEffectiveAsync("acme", "fallback", allowGlobalFallback: true))!.Kind);
    }

    [Fact]
    public async Task Identity_keys_are_rejected_before_provider_io_and_settings_json_is_deterministic()
    {
        await using var fixture = await Fixture.CreateAsync();
        var high = Configuration(null, "bad\ud800", "kind");
        var low = Configuration(null, "bad\udc00", "kind");
        await fixture.GlobalStore.SaveAsync(high);
        await fixture.GlobalStore.SaveAsync(low);
        Assert.Equal(high.Provider, (await fixture.GlobalStore.FindGlobalAsync(high.Provider))!.Provider);
        Assert.Equal(low.Provider, (await fixture.GlobalStore.FindGlobalAsync(low.Provider))!.Provider);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.GlobalStore.FindGlobalAsync(new string('x', 401)).AsTask());

        var first = Configuration("acme", "settings", "kind") with
        {
            Settings = new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" }
        };
        var second = first with
        {
            Settings = new Dictionary<string, string> { ["a"] = "first", ["z"] = "last" }
        };
        await fixture.TenantStore.SaveAsync(first);
        var firstJson = (await fixture.Context.TenantProviderConfigurations.SingleAsync()).SettingsJson;
        await fixture.TenantStore.SaveAsync(second);
        var secondJson = (await fixture.Context.TenantProviderConfigurations.SingleAsync()).SettingsJson;
        Assert.Equal(firstJson, secondJson);
    }

    [Fact]
    public async Task Kind_is_not_subject_to_the_identity_key_length_limit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var kind = string.Concat(new string('k', 800), "\ud800");
        var configuration = Configuration("acme", "long-kind", kind);

        await fixture.TenantStore.SaveAsync(configuration);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());

        Assert.Equal(kind, (await fixture.TenantStore.FindForTenantAsync("acme", "long-kind"))!.Kind);
    }

    [Fact]
    public async Task Canonical_lookup_applies_every_unicode16_garay_case_pair()
    {
        await using var fixture = await Fixture.CreateAsync();
        for (var capital = 0x10D50; capital <= 0x10D65; capital++)
        {
            var upper = char.ConvertFromUtf32(capital);
            var lower = char.ConvertFromUtf32(capital + 0x20);
            await fixture.GlobalStore.SaveAsync(Configuration(null, lower, $"garay-{capital:x}"));

            var loaded = Assert.IsType<ProviderConfigurationRecord>(
                await fixture.GlobalStore.FindGlobalAsync(upper));
            Assert.Equal(lower, loaded.Provider);
        }

        var upperComposite = string.Concat("x", "\uD800", char.ConvertFromUtf32(0x10D50), "y");
        var lowerComposite = string.Concat("x", "\uD800", char.ConvertFromUtf32(0x10D70), "y");
        await fixture.GlobalStore.SaveAsync(Configuration(null, lowerComposite, "garay-surrogate"));
        Assert.Equal(lowerComposite,
            (await fixture.GlobalStore.FindGlobalAsync(upperComposite))!.Provider);
    }

    [Fact]
    public void Registration_replaces_only_the_two_provider_configuration_contracts()
    {
        var services = new ServiceCollection();
        services.AddIdentityProviderConfigurationEntityFrameworkCore(new IdentityProviderConfigurationEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRevisionAwareProviderConfigurationStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IdentityProviderConfigurationDbContext));
        services.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRevisionAwareProviderConfigurationStore));
    }

    [Fact]
    public async Task Registration_resolves_both_contracts_to_one_scoped_EF_store()
    {
        var services = new ServiceCollection();
        services.AddIdentityProviderConfigurationEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        provider.GetRequiredService<IStartupValidator>().Validate();
        var primary = scope.ServiceProvider.GetRequiredService<IProviderConfigurationStore>();
        var revisionAware = scope.ServiceProvider.GetRequiredService<IRevisionAwareProviderConfigurationStore>();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());
        Assert.IsType<EfProviderConfigurationStore>(primary);
        Assert.Same(primary, revisionAware);
        Assert.IsType<IdentityProviderConfigurationSqliteDbContext>(
            scope.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>());
    }

    [Fact]
    public async Task Registration_preserves_a_host_provided_access_context_accessor()
    {
        var expected = new FakeAccessAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("host")));
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextAccessor>(expected);
        services.AddIdentityProviderConfigurationEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        Assert.Same(expected, scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());
    }

    [Fact]
    public async Task Groundwork_registration_resolves_both_contracts_to_one_scoped_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGroundworkStorageSessionSource, UnusedGroundworkStorageSessionSource>();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new FakeAccessAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("acme"))));
        services.AddGroundworkIdentityStores();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        provider.GetRequiredService<IStartupValidator>().Validate();
        var primary = scope.ServiceProvider.GetRequiredService<IProviderConfigurationStore>();
        var revisionAware = scope.ServiceProvider.GetRequiredService<IRevisionAwareProviderConfigurationStore>();

        Assert.IsType<GroundworkProviderConfigurationStore>(primary);
        Assert.Same(primary, revisionAware);
    }

    [Fact]
    public async Task Public_feature_binds_the_module_history_options_without_merging_OpenIddict()
    {
        var services = new ServiceCollection();
        new IdentityProviderConfigurationEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        provider.GetRequiredService<IStartupValidator>().Validate();
        var context = Assert.IsType<IdentityProviderConfigurationSqliteDbContext>(
            scope.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>());
        var primary = scope.ServiceProvider.GetRequiredService<IProviderConfigurationStore>();
        var revisionAware = scope.ServiceProvider.GetRequiredService<IRevisionAwareProviderConfigurationStore>();
        var relational = context.GetService<IDbContextOptions>().Extensions
            .OfType<RelationalOptionsExtension>()
            .Single();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());
        Assert.IsType<EfProviderConfigurationStore>(primary);
        Assert.Same(primary, revisionAware);
        Assert.Equal(IdentityProviderConfigurationEfModule.HistoryTableName, relational.MigrationsHistoryTableName);
        Assert.Equal(typeof(IdentityProviderConfigurationDbContext).Assembly.GetName().Name, relational.MigrationsAssembly);
        Assert.DoesNotContain(context.Model.GetEntityTypes(), entity =>
            entity.ClrType.FullName?.Contains("OpenIddict", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Public_feature_preserves_the_inheritance_extension_seam()
    {
        var featureType = typeof(IdentityProviderConfigurationEntityFrameworkCoreFeature);
        var configureServices = featureType.GetMethod(nameof(IdentityProviderConfigurationEntityFrameworkCoreFeature.ConfigureServices));

        Assert.False(featureType.IsSealed);
        Assert.NotNull(configureServices);
        Assert.True(configureServices!.IsVirtual);
        Assert.False(configureServices.IsFinal);
    }

    [Fact]
    public void Registration_is_order_independent_and_preserves_groundwork_unrelated_stores()
    {
        var groundworkFirst = new ServiceCollection();
        groundworkFirst.AddGroundworkIdentityStores();
        var unrelatedContractTypes = new[]
        {
            typeof(IUserStore),
            typeof(IRoleStore),
            typeof(IApplicationStore),
            typeof(ICredentialStore),
            typeof(IClaimMappingStore),
            typeof(IExternalIdentityStore),
            typeof(ITenantMembershipStore)
        };
        var unrelated = groundworkFirst.Where(descriptor => unrelatedContractTypes.Contains(descriptor.ServiceType)).ToArray();
        groundworkFirst.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
        Assert.All(unrelated, descriptor => Assert.Contains(descriptor, groundworkFirst));
        Assert.Single(groundworkFirst, descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore) && descriptor.ImplementationType is null);
        Assert.DoesNotContain(groundworkFirst, descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore) && descriptor.ImplementationType?.Name.Contains("Groundwork", StringComparison.Ordinal) == true);
        AssertGroundworkUnrelatedStoresPresent(groundworkFirst);
        using (var groundworkFirstProvider = groundworkFirst.BuildServiceProvider())
            groundworkFirstProvider.GetRequiredService<IStartupValidator>().Validate();

        var efFirst = new ServiceCollection();
        efFirst.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
        efFirst.AddGroundworkIdentityStores();
        Assert.Single(efFirst, descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore) && descriptor.ImplementationType is null);
        Assert.Single(efFirst, descriptor => descriptor.ServiceType == typeof(IRevisionAwareProviderConfigurationStore) && descriptor.ImplementationType is null);
        Assert.DoesNotContain(efFirst, descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore) && descriptor.ImplementationType?.Name.Contains("Groundwork", StringComparison.Ordinal) == true);
        AssertGroundworkUnrelatedStoresPresent(efFirst);
        using var efFirstProvider = efFirst.BuildServiceProvider();
        efFirstProvider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void Invalid_or_conflicting_registration_does_not_partially_mutate_services()
    {
        var invalid = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => invalid.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "unknown" }));
        Assert.Empty(invalid);

        var services = new ServiceCollection();
        services.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "SqlServer", ConnectionString = "Server=localhost" }));
        Assert.Equal(before, services);
        Assert.Throws<InvalidOperationException>(() => services.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=other.db" }));
        Assert.Equal(before, services);
    }

    [Fact]
    public void Custom_provider_configuration_registrations_before_EF_are_rejected_without_mutation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IProviderConfigurationStore, CustomProviderConfigurationStore>();
        services.AddScoped<IRevisionAwareProviderConfigurationStore, CustomProviderConfigurationStore>();
        var before = services.ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddIdentityProviderConfigurationEntityFrameworkCore(new()
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=:memory:"
            }));

        Assert.Contains("unowned host registration", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, services);
    }

    [Fact]
    public void Custom_provider_configuration_registrations_after_Groundwork_are_rejected_without_mutation()
    {
        var services = new ServiceCollection();
        services.AddGroundworkIdentityStores();
        services.AddScoped<IProviderConfigurationStore, CustomProviderConfigurationStore>();
        services.AddScoped<IRevisionAwareProviderConfigurationStore, CustomProviderConfigurationStore>();
        var before = services.ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddIdentityProviderConfigurationEntityFrameworkCore(new()
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=:memory:"
            }));

        Assert.Contains("no longer exclusively owns", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, services);
    }

    [Fact]
    public void Custom_provider_configuration_registrations_after_EF_fail_startup_validation()
    {
        var services = new ServiceCollection();
        services.AddIdentityProviderConfigurationEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        services.AddScoped<IProviderConfigurationStore, CustomProviderConfigurationStore>();
        services.AddScoped<IRevisionAwareProviderConfigurationStore, CustomProviderConfigurationStore>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(typeof(IProviderConfigurationStore).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(IRevisionAwareProviderConfigurationStore).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_provider_configuration_registrations_after_Groundwork_fail_startup_validation()
    {
        var services = new ServiceCollection();
        services.AddGroundworkIdentityStores();
        services.AddScoped<IProviderConfigurationStore, CustomProviderConfigurationStore>();
        services.AddScoped<IRevisionAwareProviderConfigurationStore, CustomProviderConfigurationStore>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(typeof(IProviderConfigurationStore).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(IRevisionAwareProviderConfigurationStore).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_connection_string_takes_precedence_over_named_and_default_connections()
    {
        var connectionString = ResolveConfiguredConnectionString(
            new()
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=explicit.db",
                ConnectionName = "Named"
            },
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Named"] = "Data Source=named.db",
                [$"ConnectionStrings:{IdentityProviderConfigurationEfModule.DefaultConnectionName}"] = "Data Source=default.db"
            });

        Assert.Equal("Data Source=explicit.db", connectionString);
    }

    [Fact]
    public void Blank_explicit_connection_falls_through_to_named_connection()
    {
        var connectionString = ResolveConfiguredConnectionString(
            new()
            {
                Provider = "Sqlite",
                ConnectionString = "  ",
                ConnectionName = "Named"
            },
            new Dictionary<string, string?> { ["ConnectionStrings:Named"] = "Data Source=named.db" });

        Assert.Equal("Data Source=named.db", connectionString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Missing_or_blank_named_connection_fails(string? configuredConnection)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ResolveConfiguredConnectionString(
                new() { Provider = "Sqlite", ConnectionName = "Missing" },
                new Dictionary<string, string?> { ["ConnectionStrings:Missing"] = configuredConnection }));

        Assert.Contains("Missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_ElsaIdentity_connection_is_used_when_no_explicit_selection_exists()
    {
        var connectionString = ResolveConfiguredConnectionString(
            new() { Provider = "Sqlite" },
            new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{IdentityProviderConfigurationEfModule.DefaultConnectionName}"] = "Data Source=default.db"
            });

        Assert.Equal("Data Source=default.db", connectionString);
    }

    [Fact]
    public void Sqlite_uses_the_module_default_when_no_connection_is_configured()
    {
        Assert.Equal(
            IdentityProviderConfigurationEfModule.DefaultSqliteConnectionString,
            ResolveConfiguredConnectionString(new() { Provider = "Sqlite" }));
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    public void Non_Sqlite_providers_require_an_explicit_or_configured_connection(string providerName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ResolveConfiguredConnectionString(new() { Provider = providerName }));

        Assert.Contains("requires ConnectionString or ConnectionName", exception.Message, StringComparison.Ordinal);
    }

    private static string ResolveConfiguredConnectionString(
        IdentityProviderConfigurationEntityFrameworkCoreOptions options,
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

        services.AddIdentityProviderConfigurationEntityFrameworkCore(options);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>();
        return Assert.IsType<string>(context.Database.GetConnectionString());
    }

    private static void AssertGroundworkUnrelatedStoresPresent(IServiceCollection services)
    {
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IUserStore) && descriptor.ImplementationType == typeof(GroundworkUserStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRoleStore) && descriptor.ImplementationType == typeof(GroundworkRoleStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IApplicationStore) && descriptor.ImplementationType == typeof(GroundworkApplicationStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ICredentialStore) && descriptor.ImplementationType == typeof(GroundworkCredentialStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IClaimMappingStore) && descriptor.ImplementationType == typeof(GroundworkClaimMappingStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExternalIdentityStore) && descriptor.ImplementationType == typeof(GroundworkExternalIdentityStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ITenantMembershipStore) && descriptor.ImplementationType == typeof(GroundworkTenantMembershipStore));
    }

    private static ProviderConfigurationRecord Configuration(string? tenantId, string provider, string kind) => new(
        provider, tenantId, kind, true, false,
        new ProviderCapabilities(true, false, false, true, true, true, false),
        new Dictionary<string, string> { ["client_secret"] = "value:with:delimiters", ["empty"] = "", ["unicode-😀"] = "naïve" });

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly IdentityProviderConfigurationSqliteDbContext context;
        public EfProviderConfigurationStore TenantStore { get; }
        public EfProviderConfigurationStore GlobalStore { get; }
        public EfProviderConfigurationStore UnprivilegedGlobalStore { get; }
        public IdentityProviderConfigurationSqliteDbContext Context => context;
        public IRevisionAwareProviderConfigurationStore TenantRevisionStore => TenantStore;

        private Fixture(SqliteConnection connection, IdentityProviderConfigurationSqliteDbContext context, FakeAccessAccessor tenantAccess, FakeAccessAccessor globalAccess, FakeAccessAccessor ordinaryGlobalAccess)
        {
            this.connection = connection;
            this.context = context;
            TenantStore = new EfProviderConfigurationStore(context, tenantAccess);
            GlobalStore = new EfProviderConfigurationStore(context, globalAccess);
            UnprivilegedGlobalStore = new EfProviderConfigurationStore(context, ordinaryGlobalAccess);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<IdentityProviderConfigurationSqliteDbContext>().UseSqlite(connection).Options;
            var context = new IdentityProviderConfigurationSqliteDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, context,
                new FakeAccessAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("acme"))),
                new FakeAccessAccessor(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("test"))),
                new FakeAccessAccessor(PersistenceAccessContext.Global));
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeAccessAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class CustomProviderConfigurationStore :
        IProviderConfigurationStore,
        IRevisionAwareProviderConfigurationStore
    {
        public ValueTask<ProviderConfigurationRecord?> FindGlobalAsync(string provider, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ProviderConfigurationRecord?> FindForTenantAsync(string tenantId, string provider, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask SaveAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindGlobalWithRevisionAsync(string provider, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindForTenantWithRevisionAsync(string tenantId, string provider, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(
            ProviderConfigurationRecord configuration,
            string? expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedGroundworkStorageSessionSource : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            throw new NotSupportedException();

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) => throw new NotSupportedException();
    }
}
