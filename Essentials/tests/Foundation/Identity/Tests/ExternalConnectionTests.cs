using System.Globalization;
using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
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

    [Fact]
    public async Task Same_key_database_record_requires_explicit_full_override_and_has_predictable_archive_restore()
    {
        var baseline = Record("company", enabled: true) with
        {
            Settings = new Dictionary<string, string> { ["ready"] = "yes", ["baseline-only"] = "no-inheritance" },
            Secrets = new Dictionary<string, Elsa.Secrets.Core.Models.SecretReference> { ["clientSecret"] = new("deployment-secret") },
            Admission = new AdmissionPolicy { Type = "create-user", DefaultRoleIds = ["deployment-role"] }
        };
        var catalog = new MemoryCatalog();
        using var services = Create(catalog, baseline: [baseline], localRecovery: true, overrides: true);
        var writer = services.GetRequiredService<IConnectionWriter>();
        var registry = services.GetRequiredService<IConnectionRegistry>();
        var created = await writer.ApplyAsync(new(ConnectionChange.Create, null, Record("company", enabled: true)), Actor());
        Assert.True(created.Saved);
        var snapshot = await registry.ReadAsync();
        var stored = snapshot.Connections.Single(view => view.Source == ConnectionSource.Database);
        Assert.False(stored.Record.Enabled); // All new ordinary records are disabled drafts.
        Assert.False(stored.Effective);
        Assert.Equal("deployment:company", stored.ShadowedById);
        Assert.True(snapshot.Connections.Single(view => view.Source == ConnectionSource.Deployment).Effective);

        var enabled = await writer.ApplyAsync(new(ConnectionChange.Enable, created.Revision, stored.Record), Actor());
        Assert.True(enabled.Saved);
        Assert.Equal(ConnectionSource.Deployment, (await registry.ReadAsync()).Connections.Single(view => view.Effective).Source);
        stored = (await registry.ReadAsync()).Connections.Single(view => view.Source == ConnectionSource.Database);
        var implicitOverride = await writer.ApplyAsync(new(ConnectionChange.Update, enabled.Revision, stored.Record with { OverridesDeployment = true }), Actor());
        Assert.Equal("explicit-override-required", implicitOverride.Failure);
        var promoted = await writer.ApplyAsync(new(ConnectionChange.PromoteOverride, enabled.Revision, stored.Record), Actor());
        Assert.True(promoted.Saved);
        var effective = (await registry.ReadAsync()).Connections.Single(view => view.Effective);
        Assert.Equal(ConnectionSource.Database, effective.Source);
        Assert.DoesNotContain("baseline-only", effective.Record.Settings.Keys);
        Assert.Empty(effective.Record.Secrets);
        Assert.Equal("reject", effective.Record.Admission.Type);
        Assert.Empty(effective.Record.Admission.DefaultRoleIds);
        var id = effective.Record.Id;
        var disabled = await writer.ApplyAsync(new(ConnectionChange.Disable, promoted.Revision, effective.Record), Actor());
        Assert.True(disabled.Saved);
        effective = (await registry.ReadAsync()).Connections.Single(view => view.Effective);
        Assert.False(effective.Record.Enabled);
        Assert.Equal(ConnectionSource.Database, effective.Source);
        var archived = await writer.ApplyAsync(new(ConnectionChange.Archive, disabled.Revision, effective.Record), Actor());
        Assert.True(archived.Saved);
        Assert.Equal(ConnectionSource.Deployment, (await registry.ReadAsync()).Connections.Single(view => view.Effective).Source);
        var restored = await writer.ApplyAsync(new(ConnectionChange.Restore, archived.Revision, effective.Record), Actor());
        Assert.True(restored.Saved);
        effective = (await registry.ReadAsync()).Connections.Single(view => view.Effective);
        Assert.Equal(id, effective.Record.Id);
        Assert.Equal("company", effective.Record.Key);
        Assert.False(effective.Record.Enabled);
        var removed = await writer.ApplyAsync(new(ConnectionChange.Remove, restored.Revision, effective.Record), Actor());
        Assert.True(removed.Saved);
        Assert.Equal(ConnectionSource.Deployment, Assert.Single((await registry.ReadAsync()).Connections).Source);
    }

    [Fact]
    public async Task Disabled_override_cannot_replace_the_final_usable_path_and_forbidden_override_cannot_promote()
    {
        var baseline = Record("company", enabled: true);
        var catalog = new MemoryCatalog();
        using var services = Create(catalog, baseline: [baseline], overrides: true);
        var result = await services.GetRequiredService<IConnectionWriter>().ApplyAsync(
            new(ConnectionChange.CreateOverride, null, Record("company", enabled: false)), Actor());
        Assert.Equal("final-login-path", result.Failure);
        Assert.Empty((await catalog.ReadAsync()).Records);
        using var forbidden = Create(new MemoryCatalog(), baseline: [baseline], localRecovery: true);
        result = await forbidden.GetRequiredService<IConnectionWriter>().ApplyAsync(
            new(ConnectionChange.CreateOverride, null, Record("company", enabled: true)), Actor());
        Assert.Equal("deployment-override-forbidden", result.Failure);
    }

    [Fact]
    public async Task Concurrent_disables_use_catalog_CAS_and_a_fresh_retry_guards_the_remaining_path()
    {
        var catalog = new MemoryCatalog([Record("a", true), Record("b", true)], synchronizeFirstTwoReads: true);
        using var services = Create(catalog);
        var writer = services.GetRequiredService<IConnectionWriter>();
        var results = await Task.WhenAll(
            writer.ApplyAsync(new(ConnectionChange.Disable, "1", Record("a", true)), Actor()).AsTask(),
            writer.ApplyAsync(new(ConnectionChange.Disable, "1", Record("b", true)), Actor()).AsTask());
        Assert.Single(results, result => result.Saved);
        Assert.Equal("revision-conflict", Assert.Single(results, result => !result.Saved).Failure);
        var current = await catalog.ReadAsync();
        var remaining = Assert.Single(current.Records, record => record.Enabled);
        var retry = await writer.ApplyAsync(new(ConnectionChange.Disable, current.Revision, remaining), Actor());
        Assert.Equal("final-login-path", retry.Failure);
        Assert.Single((await catalog.ReadAsync()).Records, record => record.Enabled);
    }

    [Fact]
    public async Task An_invalid_enabled_provider_is_not_counted_as_recovery_and_mutations_require_explicit_lifecycle()
    {
        var good = Record("good", true);
        var broken = Record("broken", true) with { Settings = new Dictionary<string, string> { ["ready"] = "no" } };
        var catalog = new MemoryCatalog([good, broken]);
        using var services = Create(catalog);
        var writer = services.GetRequiredService<IConnectionWriter>();
        Assert.Equal("final-login-path", (await writer.ApplyAsync(new(ConnectionChange.Disable, "1", good), Actor())).Failure);
        Assert.Equal("explicit-lifecycle-required", (await writer.ApplyAsync(new(ConnectionChange.Update, "1", good with { Enabled = false }), Actor())).Failure);
        Assert.Equal("explicit-lifecycle-required", (await writer.ApplyAsync(new(ConnectionChange.Update, "1", good with { ArchivedAt = DateTimeOffset.UtcNow }), Actor())).Failure);
        Assert.Equal("connection-key-immutable", (await writer.ApplyAsync(new(ConnectionChange.Update, "1", good with { Key = "renamed" }), Actor())).Failure);
    }

    [Fact]
    public async Task Valid_configuration_without_an_eligible_identity_is_not_a_recovery_path()
    {
        var good = Record("existing", true);
        var pending = Record("new-provider", true) with
        { Settings = new Dictionary<string, string> { ["ready"] = "yes", ["no-eligible-user"] = "yes" } };
        var catalog = new MemoryCatalog([good, pending]);
        using var services = Create(catalog);
        var result = await services.GetRequiredService<IConnectionWriter>().ApplyAsync(
            new(ConnectionChange.Disable, "1", good), Actor());
        Assert.Equal("final-login-path", result.Failure);
        Assert.True((await catalog.ReadAsync()).Records.Single(record => record.Key == "existing").Enabled);
    }

    [Fact]
    public async Task A_node_unable_to_validate_an_existing_provider_cannot_bypass_final_path_protection()
    {
        var existing = Record("existing", true) with { Settings = new Dictionary<string, string> { ["ready"] = "no" } };
        var catalog = new MemoryCatalog([existing]);
        using var services = Create(catalog);
        var result = await services.GetRequiredService<IConnectionWriter>().ApplyAsync(
            new(ConnectionChange.Disable, "1", existing), Actor());
        Assert.Equal("final-login-path", result.Failure);
        Assert.True(Assert.Single((await catalog.ReadAsync()).Records).Enabled);
    }

    [Fact]
    public async Task Source_change_assessment_shows_the_full_effect_without_saving_and_stale_confirmation_is_rejected()
    {
        var baseline = Record("company", true);
        var stored = Record("company", true);
        var catalog = new MemoryCatalog([stored]);
        using var services = Create(catalog, baseline: [baseline], overrides: true, localRecovery: true);
        var writer = services.GetRequiredService<IConnectionWriter>();
        var request = new ConnectionWrite(ConnectionChange.PromoteOverride, "1", stored);
        var assessment = await writer.AssessAsync(request, Actor());
        Assert.True(assessment.CanApply);
        Assert.Equal(ConnectionSource.Database, assessment.Proposed!.Connections.Single(view => view.Effective).Source);
        var unchanged = await catalog.ReadAsync();
        Assert.Equal("1", unchanged.Revision);
        Assert.False(Assert.Single(unchanged.Records).OverridesDeployment);
        Assert.True((await writer.ApplyAsync(new(ConnectionChange.Update, "1", stored with { DisplayName = "changed elsewhere" }), Actor())).Saved);
        Assert.Equal("revision-conflict", (await writer.ApplyAsync(request, Actor())).Failure);
        Assert.Equal(ConnectionSource.Deployment, (await services.GetRequiredService<IConnectionRegistry>().ReadAsync()).Connections.Single(view => view.Effective).Source);
    }

    [Fact]
    public async Task Unauthorized_or_other_tenant_management_never_reads_the_global_catalog()
    {
        var catalog = new MemoryCatalog();
        using var services = Create(catalog);
        var writer = services.GetRequiredService<IConnectionWriter>();
        Assert.Equal("not-authorized", (await writer.ApplyAsync(new(ConnectionChange.Create, null, Record("a")), Actor(manage: false))).Failure);
        Assert.Equal("not-authorized", (await writer.ApplyAsync(new(ConnectionChange.Create, null, Record("a")), Actor(tenant: "other"))).Failure);
        Assert.Equal(0, catalog.Reads);
    }

    [Fact]
    public async Task Inline_credentials_are_rejected_even_in_disabled_drafts_and_presentation_does_not_change_material_revision()
    {
        var catalog = new MemoryCatalog();
        using var services = Create(catalog);
        var writer = services.GetRequiredService<IConnectionWriter>();
        foreach (var key in new[] { "clientSecret", "client_secret", "Password", "refresh-token" })
            Assert.Equal("invalid-configuration", (await writer.ApplyAsync(
                new(ConnectionChange.Create, null, Record("a") with { Settings = new Dictionary<string, string> { [key] = "canary" } }), Actor())).Failure);
        var created = await writer.ApplyAsync(new(ConnectionChange.Create, null, Record("a")), Actor());
        Assert.True(created.Saved);
        var before = Assert.Single((await services.GetRequiredService<IConnectionRegistry>().ReadAsync()).Connections);
        Assert.True((await writer.ApplyAsync(new(ConnectionChange.Update, created.Revision,
            before.Record with { DisplayName = "A new name", Preferred = true, DisplayOrder = 10 }), Actor())).Saved);
        var after = Assert.Single((await services.GetRequiredService<IConnectionRegistry>().ReadAsync()).Connections);
        Assert.Equal(before.MaterialRevision, after.MaterialRevision);
    }

    [Fact]
    public async Task Role_assignment_cannot_exceed_the_editors_permissions_or_deployment_allow_list()
    {
        var catalog = new MemoryCatalog();
        var roles = new TestRoles(new RoleRecord("admin", "tenant-a", "Admin", null, new HashSet<string> { "*" }, false),
            new RoleRecord("reader", "tenant-a", "Reader", null, new HashSet<string> { DefaultIdentityPermissionKeys.IdentityProvidersRead }, false));
        using var services = Create(catalog, configure: collection => collection.AddSingleton<IRoleStore>(roles));
        var roleDraft = Record("a") with { Admission = new AdmissionPolicy { Type = "create-user", DefaultRoleIds = ["admin"] } };
        var result = await services.GetRequiredService<IConnectionWriter>().ApplyAsync(new(ConnectionChange.Create, null, roleDraft), Actor());
        Assert.Equal("role-delegation-forbidden", result.Failure);
        Assert.Empty((await catalog.ReadAsync()).Records);
        var permitted = roleDraft with { Admission = roleDraft.Admission with { DefaultRoleIds = ["reader"] } };
        Assert.True((await services.GetRequiredService<IConnectionWriter>().ApplyAsync(new(ConnectionChange.Create, null, permitted), Actor())).Saved);
        using var restricted = Create(new MemoryCatalog(), allowedRoles: ["reader"], configure: collection => collection.AddSingleton<IRoleStore>(roles));
        Assert.Equal("role-delegation-forbidden", (await restricted.GetRequiredService<IConnectionWriter>().ApplyAsync(
            new(ConnectionChange.Create, null, roleDraft), Actor(superuser: true))).Failure);
    }

    private static ServiceProvider Create(MemoryCatalog catalog, IReadOnlyList<ConnectionRecord>? baseline = null,
        bool database = true, bool overrides = false, bool localRecovery = false, Action<IServiceCollection>? configure = null,
        IReadOnlyList<string>? allowedRoles = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionCatalogStore>(catalog);
        services.AddSingleton<IConnectionReadiness>(new TestReadiness());
        services.AddSingleton<ILocalLoginRecovery>(new TestRecovery(localRecovery));
        services.AddSingleton<IConnectionRecovery>(new TestExternalRecovery());
        configure?.Invoke(services);
        services.AddFoundationExternalAuthentication(options =>
        {
            options.TenantId = "tenant-a";
            options.AllowDatabaseManagement = database;
            options.AllowDeploymentOverrides = overrides;
            options.AllowedRoleIds = allowedRoles?.ToList() ?? [];
            options.Connections = baseline?.ToList() ?? [];
        });
        ImmutableConnectionAuthorization.Register(services);
        services.AddNormalizedAuthenticationType("connection-test");
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Actor(bool manage = true, string tenant = "tenant-a", bool superuser = false) => new(new ClaimsIdentity(
        new[] { new Claim(IdentityClaimTypes.Normalized, "v1"), new Claim(IdentityClaimTypes.UserId, "administrator"),
            new Claim(ClaimTypes.NameIdentifier, "administrator"), new Claim(IdentityClaimTypes.TenantId, tenant) }
            .Concat(manage ? [new Claim(IdentityClaimTypes.Permission, DefaultIdentityPermissionKeys.IdentityProvidersManage)] : [])
            .Concat(superuser ? [new Claim(IdentityClaimTypes.Permission, "*")] : []), "connection-test"));

    private static ConnectionRecord Record(string key, bool enabled = false) => new()
    { Id = "db:" + key, Key = key, DisplayName = key, Enabled = enabled, Settings = new Dictionary<string, string> { ["ready"] = "yes" } };

    private sealed class TestReadiness : IConnectionReadiness
    {
        public ValueTask<bool> IsReadyAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(connection.Settings.TryGetValue("ready", out var ready) && ready == "yes");
    }
    private sealed class TestExternalRecovery : IConnectionRecovery
    {
        public ValueTask<bool> IsUsableAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) => ValueTask.FromResult(!connection.Settings.ContainsKey("no-eligible-user"));
        public ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ConnectionRecord connection, ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<IamRevisionGuard>?>(connection.Settings.ContainsKey("no-eligible-user") ? null : []);
    }
    private sealed class TestRecovery(bool usable) : ILocalLoginRecovery
    {
        public ValueTask<bool> IsUsableAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(usable);
        public ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<IamRevisionGuard>?>(usable ? [] : null);
    }

    private sealed class TestRoles(params RoleRecord[] roles) : IRoleStore
    {
        public ValueTask<RoleRecord?> FindAsync(string tenantId, string roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult(roles.SingleOrDefault(role => role.Id == roleId && role.TenantId == tenantId));
        public ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<RoleRecord>>(roles);
        public ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MemoryCatalog(IReadOnlyList<ConnectionRecord>? initial = null, bool synchronizeFirstTwoReads = false) : IConnectionCatalogStore
    {
        public bool SupportsIdentityRevisionGuards => true;
        private readonly object gate = new();
        private ConnectionRecord[] records = initial?.ToArray() ?? [];
        private long version = initial is null ? 0 : 1;
        private readonly TaskCompletionSource readBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads;
        public async ValueTask<ConnectionCatalog> ReadAsync(CancellationToken cancellationToken = default)
        {
            ConnectionCatalog snapshot;
            int read;
            lock (gate) { snapshot = new(records.ToArray(), Revision); read = ++Reads; }
            if (synchronizeFirstTwoReads && read <= 2)
            {
                if (read == 2) readBarrier.TrySetResult();
                await readBarrier.Task.WaitAsync(cancellationToken);
            }
            return snapshot;
        }
        public ValueTask<CatalogSaveResult> SaveAsync(ConnectionCatalog catalog, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                if (catalog.Revision != Revision) return ValueTask.FromResult(new CatalogSaveResult(false));
                records = catalog.Records.ToArray(); version++;
                return ValueTask.FromResult(new CatalogSaveResult(true, Revision));
            }
        }
        private string? Revision => version == 0 ? null : version.ToString(CultureInfo.InvariantCulture);
    }
}
