using System.Data.Common;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

public sealed partial class IdentityProviderConfigurationEntityFrameworkCoreBehaviorTests
{
    [Fact]
    public async Task Coordinated_provider_write_commits_its_catalog_and_IAM_guards_without_changing_security_or_permissions()
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var before = await fixture.Snapshots();
        var result = await fixture.Store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration with
            { Settings = new Dictionary<string, string> { ["catalog"] = "changed" } }, fixture.Revision, await fixture.Guards());
        Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
        Assert.NotEqual(fixture.Revision, result.Revision);
        Assert.Equal("changed", (await fixture.Store.FindGlobalAsync("external-authentication"))!.Settings["catalog"]);
        var after = await fixture.Snapshots();
        Assert.NotEqual(before.User.Revision, after.User.Revision);
        Assert.NotEqual(before.Role.Revision, after.Role.Revision);
        Assert.True(before.User.Record.RoleIds.SetEquals(after.User.Record.RoleIds));
        Assert.True(before.User.Record.DirectPermissions.SetEquals(after.User.Record.DirectPermissions));
        Assert.Equal(before.User.Record, after.User.Record with { RoleIds = before.User.Record.RoleIds, DirectPermissions = before.User.Record.DirectPermissions });
        Assert.True(before.Role.Record.Permissions.SetEquals(after.Role.Record.Permissions));
        Assert.Equal(before.Role.Record, after.Role.Record with { Permissions = before.Role.Record.Permissions });
        Assert.Equal(before.Stamp, after.Stamp);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("role")]
    [InlineData("catalog")]
    [InlineData("tenant")]
    public async Task A_stale_IAM_or_catalog_snapshot_rolls_back_every_guard_and_provider_change(string changed)
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var guards = await fixture.Guards();
        if (changed == "user") await fixture.Users.SetStatusAsync("tenant-a", "recovery-user", UserStatus.Disabled);
        if (changed == "role")
        {
            var role = (await fixture.Roles.FindAsync("tenant-a", "operators"))!;
            await fixture.Roles.SaveAsync(role with { Permissions = new HashSet<string>() });
        }
        if (changed == "catalog") await fixture.Store.SaveAsync(fixture.Configuration with { Enabled = false });
        if (changed == "tenant") guards[0] = new(IamRevisionGuardKind.User, "another-tenant", guards[0].Id, guards[0].Revision);
        var before = await fixture.Snapshots();
        var catalog = (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!;
        var result = await fixture.Store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration with
            { Settings = new Dictionary<string, string> { ["catalog"] = "must-not-save" } }, fixture.Revision, guards);
        Assert.Equal(IamRevisionSaveStatus.Conflict, result.Status);
        Assert.Equal(catalog.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
        Assert.Equal("initial", (await fixture.Store.FindGlobalAsync("external-authentication"))!.Settings["catalog"]);
        var after = await fixture.Snapshots();
        Assert.Equal(before.User.Revision, after.User.Revision);
        Assert.Equal(before.Role.Revision, after.Role.Revision);
        Assert.Equal(before.Stamp, after.Stamp);
    }

    [Theory]
    [InlineData("transaction")]
    [InlineData("pending")]
    [InlineData("split-target")]
    [InlineData("unprivileged")]
    [InlineData("supplied-connection")]
    [InlineData("runtime-target")]
    public async Task Coordinated_provider_writes_refuse_unowned_or_unprovable_transaction_boundaries(string boundary)
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var guards = await fixture.Guards();
        if (boundary == "transaction")
        {
            await using var caller = await fixture.Iam.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, guards).AsTask());
            Assert.NotNull(fixture.Iam.Database.CurrentTransaction);
        }
        else if (boundary == "pending")
        {
            var user = await fixture.Iam.Users.SingleAsync(); user.DisplayName = "unsaved-caller-change";
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, guards).AsTask());
            Assert.True(fixture.Iam.ChangeTracker.HasChanges()); fixture.Iam.ChangeTracker.Clear();
        }
        else if (boundary == "split-target")
        {
            await using var different = CreateContext(fixture.Path + ".different-target");
            var store = new EfProviderConfigurationStore(different, fixture.GlobalAccess, fixture.Iam);
            Assert.False(store.SupportsIdentityRevisionGuards);
            await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, guards).AsTask());
            Assert.False(File.Exists(fixture.Path + ".different-target"));
        }
        else if (boundary == "supplied-connection")
        {
            await using var connection = new SqliteConnection(ConnectionTo(fixture.Path));
            await using var supplied = new IdentityProviderConfigurationSqliteDbContext(new DbContextOptionsBuilder<IdentityProviderConfigurationSqliteDbContext>().UseSqlite(connection).Options);
            var store = new EfProviderConfigurationStore(supplied, fixture.GlobalAccess, fixture.Iam);
            Assert.False(store.SupportsIdentityRevisionGuards);
            await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, guards).AsTask());
            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
            await Assert.ThrowsAsync<EfSharedTransactionTargetMismatchException>(() => EfSharedTransaction.BeginSerializableAsync([supplied, fixture.Iam]));
            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        }
        else if (boundary == "runtime-target")
        {
            var original = ConnectionTo(fixture.Path); var changed = ConnectionTo(fixture.Path + ".runtime-target");
            fixture.Providers.Database.SetConnectionString(changed); fixture.Iam.Database.SetConnectionString(changed);
            try
            {
                Assert.False(fixture.Store.SupportsIdentityRevisionGuards);
                await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, guards).AsTask());
                await Assert.ThrowsAsync<EfSharedTransactionTargetMismatchException>(() => EfSharedTransaction.BeginSerializableAsync([fixture.Providers, fixture.Iam]));
                Assert.False(File.Exists(fixture.Path + ".runtime-target"));
            }
            finally
            { fixture.Providers.Database.SetConnectionString(original); fixture.Iam.Database.SetConnectionString(original); }
        }
        else
        {
            var store = new EfProviderConfigurationStore(fixture.Providers, new FixedAccess(PersistenceAccessContext.Global), fixture.Iam);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, guards).AsTask());
        }
        Assert.Equal(fixture.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
    }

    [Fact]
    public async Task A_provider_save_failure_rolls_back_the_already_advanced_IAM_guards()
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var guards = await fixture.Guards(); var before = await fixture.Snapshots();
        await using var faulted = CreateContext(fixture.Path, new CoordinatedSaveFault());
        var store = new EfProviderConfigurationStore(faulted, fixture.GlobalAccess, fixture.Iam);
        await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
            store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, guards).AsTask());
        var after = await fixture.Snapshots();
        Assert.Equal(before.User.Revision, after.User.Revision);
        Assert.Equal(before.Role.Revision, after.Role.Revision);
        Assert.Equal(fixture.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
    }

    [Fact]
    public async Task Two_independent_coordinated_writers_cannot_commit_the_same_recovery_and_catalog_snapshot()
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        await using var otherProviders = CreateContext(fixture.Path);
        await using var otherIam = new IdentityIamSqliteDbContext(new DbContextOptionsBuilder<IdentityIamSqliteDbContext>().UseSqlite(ConnectionTo(fixture.Path)).Options);
        var other = new EfProviderConfigurationStore(otherProviders, fixture.GlobalAccess, otherIam);
        var guards = await fixture.Guards();
        var results = await Task.WhenAll(Task.Run(async () => await fixture.Store.SaveWithIdentityRevisionGuardsAsync(
                fixture.Configuration with { Settings = new Dictionary<string, string> { ["catalog"] = "first" } }, fixture.Revision, guards)),
            Task.Run(async () => await other.SaveWithIdentityRevisionGuardsAsync(
                fixture.Configuration with { Settings = new Dictionary<string, string> { ["catalog"] = "second" } }, fixture.Revision, guards)));
        var saved = Assert.Single(results, result => result.Status == IamRevisionSaveStatus.Saved);
        Assert.Single(results, result => result.Status == IamRevisionSaveStatus.Conflict);
        Assert.Equal(saved.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("999.0.0", false)]
    public async Task Coordinated_revision_guards_preserve_readable_old_payloads_and_refuse_unknown_future_payloads(string schema, bool allowed)
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var guards = await fixture.Guards();
        var before = await fixture.Snapshots();
        await fixture.Iam.Roles.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SchemaVersion, schema));
        if (allowed) guards = await fixture.Guards();
        if (allowed)
            Assert.Equal(IamRevisionSaveStatus.Saved, (await fixture.Store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, guards)).Status);
        else
            await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() =>
                fixture.Store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, guards).AsTask());
        Assert.Equal(schema, await fixture.Iam.Roles.AsNoTracking().Select(row => row.SchemaVersion).SingleAsync());
        if (!allowed)
        {
            Assert.Equal(before.User.Revision, (await fixture.Users.FindWithRevisionAsync("tenant-a", "recovery-user"))!.Revision);
            Assert.Equal(fixture.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
        }
    }

    [Theory]
    [InlineData("user")]
    [InlineData("role")]
    public async Task A_recreated_IAM_root_with_the_same_numeric_revision_cannot_reuse_a_previous_guard(string kind)
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        IamRevisionGuard guard;
        if (kind == "user")
        {
            var before = (await fixture.Users.FindWithRevisionAsync("tenant-a", "recovery-user"))!;
            guard = (await fixture.Users.FindRevisionGuardAsync("tenant-a", "recovery-user"))!;
            Assert.True(await fixture.Users.DeleteAsync("tenant-a", "recovery-user"));
            await fixture.Users.SaveAsync(before.Record);
            Assert.Equal(before.Revision, (await fixture.Users.FindWithRevisionAsync("tenant-a", "recovery-user"))!.Revision);
            Assert.NotEqual(guard.Revision, (await fixture.Users.FindRevisionGuardAsync("tenant-a", "recovery-user"))!.Revision);
        }
        else
        {
            var role = new RoleRecord("unassigned", "tenant-a", "Unassigned", null, new HashSet<string> { "identity/users:read" }, false);
            await fixture.Roles.SaveAsync(role);
            var before = (await fixture.Roles.FindWithRevisionAsync("tenant-a", role.Id))!;
            guard = (await fixture.Roles.FindRevisionGuardAsync("tenant-a", role.Id))!;
            var aggregates = new EfIdentityAuthorityAggregateCoordinator(fixture.Iam,
                new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            Assert.Equal(EfIdentityWriteStatus.Deleted, (await aggregates.DeleteRoleAsync("tenant-a", role.Id, 1)).Status);
            await fixture.Roles.SaveAsync(role with { Permissions = new HashSet<string> { "*" } });
            Assert.Equal(before.Revision, (await fixture.Roles.FindWithRevisionAsync("tenant-a", role.Id))!.Revision);
            Assert.NotEqual(guard.Revision, (await fixture.Roles.FindRevisionGuardAsync("tenant-a", role.Id))!.Revision);
        }
        var result = await fixture.Store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, [guard]);
        Assert.Equal(IamRevisionSaveStatus.Conflict, result.Status);
        Assert.Equal(fixture.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
    }

    private sealed class CoordinatedSaveFault : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("coordinated-provider-save-private-canary");
    }

    [Fact]
    public async Task A_lost_coordinated_commit_acknowledgement_stays_unknown_and_never_reports_a_plain_conflict()
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var guards = await fixture.Guards(); var before = await fixture.Snapshots();
        await using var faulted = CreateContext(fixture.Path, new CoordinatedCommitAcknowledgementLoss());
        var store = new EfProviderConfigurationStore(faulted, fixture.GlobalAccess, fixture.Iam);
        await Assert.ThrowsAsync<EfCommitOutcomeUnknownException>(() =>
            store.SaveWithIdentityRevisionGuardsAsync(fixture.Configuration, fixture.Revision, guards).AsTask());
        // The controlled failure occurs after the physical commit. Every row must have committed
        // together, and callers must inspect current state instead of blindly replaying the write.
        var after = await fixture.Snapshots();
        Assert.NotEqual(before.User.Revision, after.User.Revision);
        Assert.NotEqual(before.Role.Revision, after.Role.Revision);
        Assert.Equal(before.Stamp, after.Stamp);
        Assert.NotEqual(fixture.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
    }

    private sealed class CoordinatedCommitAcknowledgementLoss : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) => throw new DbUpdateConcurrencyException("coordinated-commit-ack-private-canary");
    }

    private sealed class CoordinatedFixture : IAsyncDisposable
    {
        public string Path { get; } = TemporaryDatabasePath();
        public IdentityIamSqliteDbContext Iam { get; private set; } = null!;
        public IdentityProviderConfigurationSqliteDbContext Providers { get; private set; } = null!;
        public FixedAccess GlobalAccess { get; } = new(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("test-coordinated-write")));
        public EfProviderConfigurationStore Store { get; private set; } = null!;
        public EfUserStore Users { get; private set; } = null!;
        public EfRoleStore Roles { get; private set; } = null!;
        public ProviderConfigurationRecord Configuration { get; } = new("external-authentication", null, "external-connections/v1", true, false,
            ProviderCapabilities.ExternalOidcDefault, new Dictionary<string, string> { ["catalog"] = "initial" });
        public string Revision { get; private set; } = null!;

        public static async Task<CoordinatedFixture> OpenAsync()
        {
            var fixture = new CoordinatedFixture();
            try
            {
                fixture.Iam = new(new DbContextOptionsBuilder<IdentityIamSqliteDbContext>().UseSqlite(ConnectionTo(fixture.Path)).Options);
                fixture.Providers = CreateContext(fixture.Path);
                await fixture.Iam.Database.MigrateAsync(); await fixture.Providers.Database.MigrateAsync();
                var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
                fixture.Users = new(fixture.Iam, access); fixture.Roles = new(fixture.Iam, access);
                await fixture.Roles.SaveAsync(new("operators", "tenant-a", "Operators", null, new HashSet<string> { "identity/providers:manage" }, false));
                await fixture.Users.SaveAsync(new("recovery-user", "tenant-a", "recovery", null, "Recovery", UserStatus.Active,
                    ResourceOwnership.Foundation, new HashSet<string> { "operators" }, new HashSet<string>()));
                fixture.Store = new(fixture.Providers, fixture.GlobalAccess, fixture.Iam);
                Assert.True(fixture.Store.SupportsIdentityRevisionGuards);
                fixture.Revision = (await fixture.Store.SaveWithRevisionAsync(fixture.Configuration, null)).Revision!;
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task<(IamRevisionedRecord<UserRecord> User, IamRevisionedRecord<RoleRecord> Role, string? Stamp)> Snapshots() =>
            ((await Users.FindWithRevisionAsync("tenant-a", "recovery-user"))!, (await Roles.FindWithRevisionAsync("tenant-a", "operators"))!,
                await Iam.Users.AsNoTracking().Select(row => row.SecurityStamp).SingleAsync());

        public async Task<IamRevisionGuard[]> Guards()
        {
            return [(await Users.FindRevisionGuardAsync("tenant-a", "recovery-user"))!,
                (await Roles.FindRevisionGuardAsync("tenant-a", "operators"))!];
        }

        public async ValueTask DisposeAsync()
        {
            if (Providers is not null) await Providers.DisposeAsync();
            if (Iam is not null) await Iam.DisposeAsync();
            DeleteDatabaseFiles(Path);
        }
    }
}
