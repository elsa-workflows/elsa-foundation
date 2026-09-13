using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UnprivilegedGlobalStore.SaveAsync(Configuration(null, "oidc", "global")).AsTask());
    }

    [Fact]
    public async Task Effective_fallback_does_not_bypass_explicit_global_access()
    {
        await using var fixture = await Fixture.CreateAsync();
        IProviderConfigurationStore store = fixture.TenantStore;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindEffectiveAsync("acme", "missing", allowGlobalFallback: true).AsTask());
    }

    [Fact]
    public async Task Identity_keys_are_rejected_before_provider_io_and_settings_json_is_deterministic()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.GlobalStore.FindGlobalAsync("bad\ud800").AsTask());
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
    }

    private static ProviderConfigurationRecord Configuration(string? tenantId, string provider, string kind) => new(
        provider, tenantId, kind, true, false,
        new ProviderCapabilities(true, false, false, true, true, true, false),
        new Dictionary<string, string> { ["client_secret"] = "value:with:delimiters", ["empty"] = "" });

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
}
