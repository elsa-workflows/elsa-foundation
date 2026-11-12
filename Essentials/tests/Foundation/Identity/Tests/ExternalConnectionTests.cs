using Elsa.Foundation.Identity.ExternalAuthentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ExternalConnectionTests
{
    [Fact]
    public async Task Activation_requires_opt_in_persistence_but_custom_catalogs_and_deployment_only_hosts_remain_valid()
    {
        var missing = new ServiceCollection();
        missing.AddFoundationExternalAuthentication(options => options.AllowDatabaseManagement = true);
        using (var provider = missing.BuildServiceProvider())
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                foreach (var initializer in provider.GetServices<CShells.Lifecycle.IShellInitializer>())
                    await initializer.InitializeAsync(CancellationToken.None);
            });
        using var configured = Create(new MemoryCatalog());
        foreach (var initializer in configured.GetServices<CShells.Lifecycle.IShellInitializer>())
            await initializer.InitializeAsync(CancellationToken.None);
        var sourceOnly = new ServiceCollection();
        sourceOnly.AddFoundationExternalAuthentication();
        using var sourceProvider = sourceOnly.BuildServiceProvider();
        foreach (var initializer in sourceProvider.GetServices<CShells.Lifecycle.IShellInitializer>())
            await initializer.InitializeAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Deployment_only_registry_does_not_read_optional_database_and_snapshots_are_detached()
    {
        var baseline = Record("company", enabled: true);
        var catalog = new MemoryCatalog();
        using var services = Create(catalog, baseline: [baseline], database: false);
        var registry = services.GetRequiredService<IConnectionRegistry>();
        var first = await registry.ReadAsync();
        Assert.False(first.DatabaseManagement);
        var view = Assert.Single(first.Connections);
        Assert.Equal(ConnectionSource.Deployment, view.Source);
        Assert.True(view.Effective);
        Assert.Equal("deployment:company", view.Record.Id);
        ((IDictionary<string, string>)view.Record.Settings)["ready"] = "no";
        Assert.Equal("yes", Assert.Single((await registry.ReadAsync()).Connections).Record.Settings["ready"]);
        Assert.Equal(0, catalog.Reads);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task Stored_connection_replaces_a_baseline_only_with_explicit_intent_and_permission(
        bool intent, bool permitted, bool expectStored)
    {
        var stored = Record("company") with { OverridesDeployment = intent };
        using var services = Create(new MemoryCatalog([stored]), [Record("company", true)], overrides: permitted);
        var effective = Assert.Single((await services.GetRequiredService<IConnectionRegistry>().ReadAsync()).Connections,
            view => view.Effective);
        Assert.Equal(expectStored ? ConnectionSource.Database : ConnectionSource.Deployment, effective.Source);
    }

    [Fact]
    public async Task Override_selection_is_full_and_disabled_shadow_archive_restore_and_removal_are_explicit()
    {
        var baseline = Record("company", true) with
        {
            Settings = new Dictionary<string, string> { ["baseline-only"] = "no inheritance" },
            Secrets = new Dictionary<string, SecretReference> { ["clientSecret"] = new("deployment-secret") },
            Admission = new AdmissionPolicy { Type = "create-user", DefaultRoleIds = ["deployment-role"] }
        };
        var stored = Record("company") with { OverridesDeployment = true };
        var catalog = new MemoryCatalog([stored]);
        using var services = Create(catalog, [baseline], overrides: true);
        var registry = services.GetRequiredService<IConnectionRegistry>();
        var effective = Assert.Single((await registry.ReadAsync()).Connections, view => view.Effective);
        Assert.Equal(ConnectionSource.Database, effective.Source);
        Assert.False(effective.Record.Enabled);
        Assert.DoesNotContain("baseline-only", effective.Record.Settings.Keys);
        Assert.Empty(effective.Record.Secrets);
        Assert.Equal("reject", effective.Record.Admission.Type);
        Assert.Empty(effective.Record.Admission.DefaultRoleIds);

        // Change the test store's source documents directly: this verifies selection, not mutation authorization.
        catalog.Replace([stored with { ArchivedAt = DateTimeOffset.UnixEpoch }]);
        Assert.Equal(ConnectionSource.Deployment, Assert.Single((await registry.ReadAsync()).Connections, view => view.Effective).Source);
        catalog.Replace([stored]);
        Assert.Equal(stored.Id, Assert.Single((await registry.ReadAsync()).Connections, view => view.Effective).Record.Id);
        catalog.Replace([]);
        Assert.Equal(ConnectionSource.Deployment, Assert.Single((await registry.ReadAsync()).Connections).Source);
    }

    [Fact]
    public async Task Deployment_allow_list_can_refuse_an_otherwise_explicit_override()
    {
        var catalog = new MemoryCatalog([Record("company") with { OverridesDeployment = true }]);
        using var services = Create(catalog, [Record("company", true)], overrides: true,
            configure: options => options.AllowedOverrideKeys = ["another"]);
        var view = await services.GetRequiredService<IConnectionRegistry>().ReadAsync();
        Assert.Equal(ConnectionSource.Deployment, Assert.Single(view.Connections, item => item.Effective).Source);
        Assert.All(view.Connections, item => Assert.False(item.OverridePermitted));
    }

    [Fact]
    public async Task Registry_rejects_duplicate_managed_identities_and_invalid_logical_keys()
    {
        var cases = new ConnectionRecord[][]
        {
            [Record("company"), Record("company") with { Id = "another" }],
            [Record("company") with { Id = "duplicate" }, Record("another") with { Id = "duplicate" }],
            [Record("Company")],
            [Record("company/path")]
        };
        foreach (var records in cases)
        {
            using var services = Create(new MemoryCatalog(records));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await services.GetRequiredService<IConnectionRegistry>().ReadAsync());
        }
    }

    [Theory]
    [InlineData("clientSecret", "inline")]
    [InlineData("client_secret", "inline")]
    [InlineData("access-token", "inline")]
    [InlineData("authority", "https://user:password@issuer.example/")]
    public async Task Disabled_drafts_cannot_carry_inline_credentials(string field, string value)
    {
        var record = Record("company") with { Settings = new Dictionary<string, string> { [field] = value } };
        using var services = Create(new MemoryCatalog([record]));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await services.GetRequiredService<IConnectionRegistry>().ReadAsync());
    }

    [Fact]
    public async Task Presentation_and_dictionary_order_do_not_change_authentication_material_revision()
    {
        var record = Record("company") with { Settings = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" } };
        var catalog = new MemoryCatalog([record]);
        using var services = Create(catalog);
        var registry = services.GetRequiredService<IConnectionRegistry>();
        var initial = Assert.Single((await registry.ReadAsync()).Connections).MaterialRevision;
        catalog.Replace([record with { DisplayName = "Renamed", DisplayOrder = 99, Preferred = true,
            Settings = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" } }]);
        Assert.Equal(initial, Assert.Single((await registry.ReadAsync()).Connections).MaterialRevision);
        catalog.Replace([record with { Settings = new Dictionary<string, string> { ["a"] = "changed" } }]);
        Assert.NotEqual(initial, Assert.Single((await registry.ReadAsync()).Connections).MaterialRevision);
    }

    private static ServiceProvider Create(MemoryCatalog catalog, IReadOnlyList<ConnectionRecord>? baseline = null,
        bool database = true, bool overrides = false, Action<ExternalAuthenticationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionCatalogStore>(catalog);
        services.AddFoundationExternalAuthentication(options =>
        {
            options.TenantId = "tenant-a";
            options.AllowDatabaseManagement = database;
            options.AllowDeploymentOverrides = overrides;
            options.Connections = baseline?.ToList() ?? [];
            configure?.Invoke(options);
        });
        return services.BuildServiceProvider();
    }

    private static ConnectionRecord Record(string key, bool enabled = false) => new()
    {
        Id = "record-" + key, Key = key, DisplayName = key, Enabled = enabled,
        Settings = new Dictionary<string, string> { ["ready"] = "yes" }
    };

    private sealed class MemoryCatalog(IReadOnlyList<ConnectionRecord>? initial = null) : IConnectionCatalogStore
    {
        private IReadOnlyList<ConnectionRecord> records = initial ?? [];
        public int Reads;
        public void Replace(IReadOnlyList<ConnectionRecord> next) => records = next;
        public ValueTask<ConnectionCatalog> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return ValueTask.FromResult(new ConnectionCatalog(records, "read-catalog"));
        }
        public ValueTask<CatalogSaveResult> SaveAsync(ConnectionCatalog catalog, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This fixture qualifies catalog reads only.");
    }
}
