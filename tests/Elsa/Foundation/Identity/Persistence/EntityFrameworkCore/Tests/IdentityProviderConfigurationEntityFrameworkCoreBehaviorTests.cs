using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

public sealed class IdentityProviderConfigurationEntityFrameworkCoreBehaviorTests
{
    [Fact]
    public async Task All_fields_and_arbitrary_utf16_settings_round_trip_losslessly()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var configuration = new ProviderConfigurationRecord(
                "OIDC\ud800",
                "Tenant\udc00",
                "external\ud800-kind",
                Enabled: false,
                IsDefault: true,
                new ProviderCapabilities(false, true, false, true, false, true, false, PermissionPropagationMode.TokenRefreshBoundary),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["key\ud800"] = "value\udc00",
                    ["unicode-😀"] = "naïve",
                    ["large"] = new string('x', 4096),
                    [""] = ""
                });

            await using (var context = CreateContext(databasePath))
                await Store(context, configuration.TenantId!).SaveAsync(configuration);

            await using var reopened = CreateContext(databasePath);
            var loaded = await Store(reopened, configuration.TenantId!).FindForTenantAsync(configuration.TenantId!, configuration.Provider);
            AssertConfiguration(configuration, Assert.IsType<ProviderConfigurationRecord>(loaded));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Framed_tenant_provider_identity_is_injective_and_global_scope_is_separate()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var first = Configuration("ab", "c", "first");
            var delimiterCollision = Configuration("a", "bc", "second");
            var global = Configuration(null, "c", "global");

            await using var context = CreateContext(databasePath);
            await Store(context, "ab").SaveAsync(first);
            await Store(context, "a").SaveAsync(delimiterCollision);
            await GlobalStore(context).SaveAsync(global);

            Assert.Equal("first", (await Store(context, "ab").FindForTenantAsync("ab", "c"))!.Kind);
            Assert.Equal("second", (await Store(context, "a").FindForTenantAsync("a", "bc"))!.Kind);
            Assert.Equal("global", (await GlobalStore(context).FindGlobalAsync("C"))!.Kind);
            Assert.Null(await Store(context, "ab").FindForTenantAsync("ab", "bc"));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Restart_and_explicit_transaction_rollback_preserve_only_committed_state()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var original = Configuration("acme", "oidc", "committed");
            await using (var seed = CreateContext(databasePath))
                await Store(seed, "acme").SaveAsync(original);

            await using (var transactionContext = CreateContext(databasePath))
            {
                await using var transaction = await transactionContext.Database.BeginTransactionAsync();
                await Store(transactionContext, "acme").SaveAsync(original with { Kind = "rolled-back" });
                await transaction.RollbackAsync();
            }

            await using var reopened = CreateContext(databasePath);
            Assert.Equal("committed", (await Store(reopened, "acme").FindForTenantAsync("acme", "oidc"))!.Kind);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Cancellation_during_save_and_before_query_leaves_no_ghost_state_or_tracking()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            using var saveCancellation = new CancellationTokenSource();
            await using (var canceledContext = CreateContext(databasePath, new CancelingSaveInterceptor(saveCancellation)))
            {
                var store = Store(canceledContext, "acme");
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    store.SaveAsync(Configuration("acme", "canceled", "never-committed"), saveCancellation.Token).AsTask());
                Assert.Empty(canceledContext.ChangeTracker.Entries());
            }

            await using (var verification = CreateContext(databasePath))
                Assert.Null(await Store(verification, "acme").FindForTenantAsync("acme", "canceled"));

            using var queryCancellation = new CancellationTokenSource();
            queryCancellation.Cancel();
            await using var queryContext = CreateContext(databasePath);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Store(queryContext, "acme").FindForTenantAsync("acme", "canceled", queryCancellation.Token).AsTask());
            Assert.Empty(queryContext.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_create_only_has_exactly_one_winner_and_loser_recovers()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var barrier = new DeterministicSaveBarrier();
            await using var first = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: true));
            await using var second = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: false));
            var results = await Task.WhenAll(
                Store(first, "acme").SaveWithRevisionAsync(Configuration("acme", "race", "winner"), null).AsTask(),
                Store(second, "acme").SaveWithRevisionAsync(Configuration("acme", "race", "loser"), null).AsTask());

            Assert.Equal(IamRevisionSaveStatus.Saved, results[0].Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict, results[1].Status);
            Assert.Empty(second.ChangeTracker.Entries());
            Assert.Equal("winner", (await Store(second, "acme").FindForTenantAsync("acme", "race"))!.Kind);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_same_revision_updates_have_exactly_one_winner_and_no_lost_update()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var original = Configuration("acme", "cas-race", "original");
            string revision;
            await using (var seed = CreateContext(databasePath))
            {
                var result = await Store(seed, "acme").SaveWithRevisionAsync(original, null);
                revision = Assert.IsType<string>(result.Revision);
            }

            var barrier = new DeterministicSaveBarrier();
            await using var first = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: true));
            await using var second = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: false));
            var results = await Task.WhenAll(
                Store(first, "acme").SaveWithRevisionAsync(original with { Kind = "winner" }, revision).AsTask(),
                Store(second, "acme").SaveWithRevisionAsync(original with { Kind = "loser" }, revision).AsTask());

            Assert.Equal(IamRevisionSaveStatus.Saved, results[0].Status);
            Assert.Equal(IamRevisionSaveStatus.Conflict, results[1].Status);
            Assert.Empty(second.ChangeTracker.Entries());
            Assert.Equal("winner", (await Store(second, "acme").FindForTenantAsync("acme", "cas-race"))!.Kind);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Read_only_revision_and_effective_queries_observe_external_updates()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var tenant = Configuration("acme", "stale-read", "original");
            var global = Configuration(null, "stale-read", "global");
            await using (var seed = CreateContext(databasePath))
            {
                await Store(seed, "acme").SaveAsync(tenant);
                await GlobalStore(seed).SaveAsync(global);
            }

            await using var reader = CreateContext(databasePath);
            var tenantReader = Store(reader, "acme");
            var globalReader = GlobalStore(reader);
            var tenantBefore = Assert.IsType<IamRevisionedRecord<ProviderConfigurationRecord>>(
                await tenantReader.FindForTenantWithRevisionAsync("acme", tenant.Provider));
            var globalBefore = Assert.IsType<IamRevisionedRecord<ProviderConfigurationRecord>>(
                await globalReader.FindGlobalWithRevisionAsync(global.Provider));
            Assert.Equal("original", (await tenantReader.FindEffectiveAsync("acme", tenant.Provider, allowGlobalFallback: true))!.Kind);
            Assert.Empty(reader.ChangeTracker.Entries());

            await using (var writer = CreateContext(databasePath))
            {
                Assert.Equal(IamRevisionSaveStatus.Saved,
                    (await Store(writer, "acme").SaveWithRevisionAsync(tenant with { Kind = "updated" }, tenantBefore.Revision)).Status);
                Assert.Equal(IamRevisionSaveStatus.Saved,
                    (await GlobalStore(writer).SaveWithRevisionAsync(global with { Kind = "updated" }, globalBefore.Revision)).Status);
            }

            var tenantAfter = Assert.IsType<IamRevisionedRecord<ProviderConfigurationRecord>>(
                await tenantReader.FindForTenantWithRevisionAsync("acme", tenant.Provider));
            var globalAfter = Assert.IsType<IamRevisionedRecord<ProviderConfigurationRecord>>(
                await globalReader.FindGlobalWithRevisionAsync(global.Provider));
            Assert.Equal("updated", tenantAfter.Record.Kind);
            Assert.Equal("updated", globalAfter.Record.Kind);
            Assert.NotEqual(tenantBefore.Revision, tenantAfter.Revision);
            Assert.NotEqual(globalBefore.Revision, globalAfter.Revision);
            Assert.Equal("updated", (await tenantReader.FindEffectiveAsync("acme", tenant.Provider, allowGlobalFallback: true))!.Kind);
            Assert.Empty(reader.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task A_reused_writer_can_apply_a_revision_observed_after_an_external_update()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var original = Configuration("acme", "reused-writer", "original");
            await using var reusedContext = CreateContext(databasePath);
            var reusedWriter = Store(reusedContext, "acme");
            var created = await reusedWriter.SaveWithRevisionAsync(original, expectedRevision: null);
            Assert.Equal(IamRevisionSaveStatus.Saved, created.Status);
            Assert.Empty(reusedContext.ChangeTracker.Entries());

            await using (var externalContext = CreateContext(databasePath))
            {
                var externalWriter = Store(externalContext, "acme");
                var external = await externalWriter.SaveWithRevisionAsync(
                    original with { Kind = "external" },
                    Assert.IsType<string>(created.Revision));
                Assert.Equal(IamRevisionSaveStatus.Saved, external.Status);
            }

            var observed = Assert.IsType<IamRevisionedRecord<ProviderConfigurationRecord>>(
                await reusedWriter.FindForTenantWithRevisionAsync("acme", original.Provider));
            Assert.Equal("external", observed.Record.Kind);

            var saved = await reusedWriter.SaveWithRevisionAsync(
                original with { Kind = "reused" },
                observed.Revision);

            Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
            Assert.Equal("reused", (await reusedWriter.FindForTenantAsync("acme", original.Provider))!.Kind);
            Assert.Empty(reusedContext.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Theory]
    [InlineData("gw:00000000000000000000")]
    [InlineData("GW:00000000000000000001")]
    [InlineData("gw:0000000000000000001")]
    [InlineData("gw:00000000000000000001 ")]
    [InlineData("gw:+0000000000000000001")]
    [InlineData("not-a-revision")]
    public async Task Malformed_revisions_are_conflicts_without_provider_io(string revision)
    {
        var options = new DbContextOptionsBuilder<IdentityProviderConfigurationSqliteDbContext>()
            .UseSqlite("Data Source=/path/that/must/not/be/opened/identity.db")
            .Options;
        await using var context = new IdentityProviderConfigurationSqliteDbContext(options);
        var result = await Store(context, "acme").SaveWithRevisionAsync(Configuration("acme", "oidc", "kind"), revision);
        Assert.Equal(IamRevisionSaveStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Global_cas_rotates_revision_and_rejects_stale_without_mutation()
    {
        await using var fixture = await FileFixture.CreateAsync();
        var original = Configuration(null, "global-cas", "original");
        await fixture.Store.SaveAsync(original);
        var first = await fixture.Store.FindGlobalWithRevisionAsync(original.Provider);
        var second = await fixture.Store.FindGlobalWithRevisionAsync(original.Provider);

        var saved = await fixture.Store.SaveWithRevisionAsync(original with { Kind = "winner" }, first!.Revision);
        var stale = await fixture.Store.SaveWithRevisionAsync(original with { Kind = "loser" }, second!.Revision);

        Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, stale.Status);
        Assert.NotEqual(first.Revision, saved.Revision);
        Assert.Equal("winner", (await fixture.Store.FindGlobalAsync(original.Provider))!.Kind);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Concurrent_unconditional_create_and_update_are_last_write_wins()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            await RunOrderedUnconditionalRaceAsync(databasePath, "unconditional-create", seed: false);
            await RunOrderedUnconditionalRaceAsync(databasePath, "unconditional-update", seed: true);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Transient_write_conflicts_retry_with_a_bound_and_clear_failed_state()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var retryOnce = new TransientSaveInterceptor(failures: 1);
            await using (var retryContext = CreateContext(databasePath, retryOnce))
            {
                await Store(retryContext, "acme").SaveAsync(Configuration("acme", "retry", "saved"));
                Assert.Equal(2, retryOnce.Attempts);
            }

            var exhaustRetries = new TransientSaveInterceptor(failures: int.MaxValue);
            await using (var failedContext = CreateContext(databasePath, exhaustRetries))
            {
                await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                    Store(failedContext, "acme").SaveAsync(Configuration("acme", "retry-failure", "never-saved")).AsTask());
                Assert.Equal(3, exhaustRetries.Attempts);
                Assert.Empty(failedContext.ChangeTracker.Entries());
            }

            await using var verification = CreateContext(databasePath);
            Assert.Null(await Store(verification, "acme").FindForTenantAsync("acme", "retry-failure"));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Create_only_transient_conflicts_retry_with_a_bound_and_leave_no_ghost_row()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var retryOnce = new TransientSaveInterceptor(failures: 1);
            await using (var retryContext = CreateContext(databasePath, retryOnce))
            {
                var result = await Store(retryContext, "acme")
                    .SaveWithRevisionAsync(Configuration("acme", "create-retry", "saved"), expectedRevision: null);
                Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
                Assert.Equal(2, retryOnce.Attempts);
            }

            var exhaustRetries = new TransientSaveInterceptor(failures: int.MaxValue);
            await using (var failedContext = CreateContext(databasePath, exhaustRetries))
            {
                await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                    Store(failedContext, "acme")
                        .SaveWithRevisionAsync(Configuration("acme", "create-retry-failure", "never-saved"), expectedRevision: null)
                        .AsTask());
                Assert.Equal(3, exhaustRetries.Attempts);
                Assert.Empty(failedContext.ChangeTracker.Entries());
            }

            await using var verification = CreateContext(databasePath);
            Assert.Null(await Store(verification, "acme").FindForTenantAsync("acme", "create-retry-failure"));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Cas_transient_conflicts_retry_with_a_bound_and_preserve_the_last_committed_row()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            var retryRecord = Configuration("acme", "cas-retry", "original");
            var failureRecord = Configuration("acme", "cas-retry-failure", "original");
            string retryRevision;
            string failureRevision;
            await using (var seed = CreateContext(databasePath))
            {
                retryRevision = Assert.IsType<string>((await Store(seed, "acme").SaveWithRevisionAsync(retryRecord, null)).Revision);
                failureRevision = Assert.IsType<string>((await Store(seed, "acme").SaveWithRevisionAsync(failureRecord, null)).Revision);
            }

            var retryOnce = new TransientSaveInterceptor(failures: 1);
            await using (var retryContext = CreateContext(databasePath, retryOnce))
            {
                var result = await Store(retryContext, "acme")
                    .SaveWithRevisionAsync(retryRecord with { Kind = "updated" }, retryRevision);
                Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
                Assert.Equal(2, retryOnce.Attempts);
            }

            var exhaustRetries = new TransientSaveInterceptor(failures: int.MaxValue);
            await using (var failedContext = CreateContext(databasePath, exhaustRetries))
            {
                await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
                    Store(failedContext, "acme")
                        .SaveWithRevisionAsync(failureRecord with { Kind = "never-saved" }, failureRevision)
                        .AsTask());
                Assert.Equal(3, exhaustRetries.Attempts);
                Assert.Empty(failedContext.ChangeTracker.Entries());
            }

            await using var verification = CreateContext(databasePath);
            Assert.Equal("updated", (await Store(verification, "acme").FindForTenantAsync("acme", retryRecord.Provider))!.Kind);
            Assert.Equal("original", (await Store(verification, "acme").FindForTenantAsync("acme", failureRecord.Provider))!.Kind);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Theory]
    [InlineData("null-settings")]
    [InlineData("non-pair-settings")]
    [InlineData("odd-utf16")]
    [InlineData("nonpositive-revision")]
    [InlineData("negative-revision")]
    public async Task Corrupt_persisted_rows_fail_through_the_stable_boundary_without_tracking(string corruption)
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var store = Store(context, "acme");
            var configuration = Configuration("acme", $"corrupt-{corruption}", "kind");
            await store.SaveAsync(configuration);
            var id = await context.TenantProviderConfigurations
                .AsNoTracking()
                .Where(entity => entity.Provider == configuration.Provider)
                .Select(entity => entity.Id)
                .SingleAsync();

            switch (corruption)
            {
                case "null-settings":
                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE \"identity_provider_configurations\" SET \"SettingsJson\" = {0} WHERE \"Id\" = {1}",
                        "null",
                        id);
                    break;
                case "non-pair-settings":
                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE \"identity_provider_configurations\" SET \"SettingsJson\" = {0} WHERE \"Id\" = {1}",
                        "[[\"AA==\"]]",
                        id);
                    break;
                case "odd-utf16":
                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE \"identity_provider_configurations\" SET \"SettingsJson\" = {0} WHERE \"Id\" = {1}",
                        "[[\"AQ==\",\"dmFsdWU=\"]]",
                        id);
                    break;
                case "nonpositive-revision":
                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE \"identity_provider_configurations\" SET \"Revision\" = 0 WHERE \"Id\" = {0}",
                        id);
                    break;
                case "negative-revision":
                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE \"identity_provider_configurations\" SET \"Revision\" = -1 WHERE \"Id\" = {0}",
                        id);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null);
            }

            context.ChangeTracker.Clear();
            async Task ReadAsync()
            {
                if (corruption is "nonpositive-revision" or "negative-revision")
                    await store.FindForTenantWithRevisionAsync("acme", configuration.Provider);
                else
                    await store.FindForTenantAsync("acme", configuration.Provider);
            }

            var exception = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(ReadAsync);
            if (corruption is "nonpositive-revision" or "negative-revision")
                Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
            else
                Assert.IsType<FormatException>(exception.InnerException);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("global")]
    [InlineData("effective")]
    [InlineData("tenant-revision")]
    [InlineData("global-revision")]
    public async Task Provider_read_failures_are_wrapped_and_leave_no_tracked_state(string operation)
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await EnsureDatabaseAsync(databasePath);
            await using var context = CreateContext(databasePath, new FailingReadInterceptor());
            var tenantStore = Store(context, "acme");
            var globalStore = GlobalStore(context);

            async Task ReadAsync()
            {
                switch (operation)
                {
                    case "tenant":
                        await tenantStore.FindForTenantAsync("acme", "oidc");
                        break;
                    case "global":
                        await globalStore.FindGlobalAsync("oidc");
                        break;
                    case "effective":
                        await tenantStore.FindEffectiveAsync("acme", "oidc", allowGlobalFallback: true);
                        break;
                    case "tenant-revision":
                        await tenantStore.FindForTenantWithRevisionAsync("acme", "oidc");
                        break;
                    case "global-revision":
                        await globalStore.FindGlobalWithRevisionAsync("oidc");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
                }
            }

            var exception = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(ReadAsync);
            Assert.IsType<SqliteException>(exception.InnerException);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static async Task RunOrderedUnconditionalRaceAsync(string databasePath, string provider, bool seed)
    {
        if (seed)
        {
            await using var seedContext = CreateContext(databasePath);
            await Store(seedContext, "acme").SaveAsync(Configuration("acme", provider, "seed"));
        }

        var barrier = new DeterministicSaveBarrier();
        await using var first = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: true));
        await using var second = CreateContext(databasePath, new OrderedSaveChangesInterceptor(barrier, isWinner: false));
        await Task.WhenAll(
            Store(first, "acme").SaveAsync(Configuration("acme", provider, "first")).AsTask(),
            Store(second, "acme").SaveAsync(Configuration("acme", provider, "second")).AsTask());

        second.ChangeTracker.Clear();
        Assert.Equal("second", (await Store(second, "acme").FindForTenantAsync("acme", provider))!.Kind);
    }

    private static ProviderConfigurationRecord Configuration(string? tenantId, string provider, string kind) => new(
        provider,
        tenantId,
        kind,
        Enabled: true,
        IsDefault: false,
        new ProviderCapabilities(true, false, true, false, true, false, true),
        new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = "value" });

    private static void AssertConfiguration(ProviderConfigurationRecord expected, ProviderConfigurationRecord actual)
    {
        Assert.Equal(expected.Provider, actual.Provider);
        Assert.Equal(expected.TenantId, actual.TenantId);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.IsDefault, actual.IsDefault);
        Assert.Equal(expected.Capabilities, actual.Capabilities);
        Assert.Equal(
            expected.Settings.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.Settings.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    private static EfProviderConfigurationStore Store(IdentityProviderConfigurationDbContext context, string tenantId) =>
        new(context, new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId))));

    private static EfProviderConfigurationStore GlobalStore(IdentityProviderConfigurationDbContext context) =>
        new(context, new FixedAccess(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("test"))));

    private static IdentityProviderConfigurationSqliteDbContext CreateContext(string databasePath, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<IdentityProviderConfigurationSqliteDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=5");
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new IdentityProviderConfigurationSqliteDbContext(builder.Options);
    }

    private static async Task EnsureDatabaseAsync(string databasePath)
    {
        await using var context = CreateContext(databasePath);
        await context.Database.EnsureCreatedAsync();
    }

    private static string TemporaryDatabasePath() =>
        Path.Join(Path.GetTempPath(), $"elsa-identity-provider-config-{Guid.NewGuid():N}.db");

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" }.Where(File.Exists))
            File.Delete(path);
    }

    private sealed class FileFixture(
        string databasePath,
        IdentityProviderConfigurationSqliteDbContext context,
        EfProviderConfigurationStore store) : IAsyncDisposable
    {
        public IdentityProviderConfigurationSqliteDbContext Context { get; } = context;
        public EfProviderConfigurationStore Store { get; } = store;

        public static async Task<FileFixture> CreateAsync()
        {
            var path = TemporaryDatabasePath();
            var context = CreateContext(path);
            try
            {
                await context.Database.EnsureCreatedAsync();
                return new FileFixture(path, context, GlobalStore(context));
            }
            catch
            {
                await context.DisposeAsync();
                DeleteDatabaseFiles(path);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            DeleteDatabaseFiles(databasePath);
        }
    }

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class CancelingSaveInterceptor(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransientSaveInterceptor(int failures) : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts <= failures)
                throw new SqliteException("database is locked", 5);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailingReadInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<DbDataReader>>(
                new SqliteException("Injected provider read failure.", 1));
    }

    private sealed class DeterministicSaveBarrier
    {
        private readonly TaskCompletionSource loserArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource winnerCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SignalLoserArrived() => loserArrived.TrySetResult();
        public Task WaitForLoserAsync(CancellationToken cancellationToken) => loserArrived.Task.WaitAsync(cancellationToken);
        public void SignalWinnerCompleted() => winnerCompleted.TrySetResult();
        public Task WaitForWinnerAsync(CancellationToken cancellationToken) => winnerCompleted.Task.WaitAsync(cancellationToken);
    }

    private sealed class OrderedSaveChangesInterceptor(
        DeterministicSaveBarrier barrier,
        bool isWinner) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (isWinner)
                await barrier.WaitForLoserAsync(cancellationToken);
            else
            {
                barrier.SignalLoserArrived();
                await barrier.WaitForWinnerAsync(cancellationToken);
            }

            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (isWinner)
                barrier.SignalWinnerCompleted();
            return ValueTask.FromResult(result);
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (isWinner)
                barrier.SignalWinnerCompleted();
            return Task.CompletedTask;
        }
    }
}
