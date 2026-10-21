using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

public sealed class SqliteEfSecretRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migrate_crud_revision_conflict_and_tenant_isolation()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = fixture.Repository;
        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        var alpha = Secret("tenant-a", "payments.api", "alpha");
        var beta = Secret("tenant-b", "payments.api", "beta");

        Assert.True(await repository.TryAddAsync(alpha));
        Assert.False(await repository.TryAddAsync(Secret("tenant-a", "payments.api", "duplicate")));
        Assert.True(await repository.TryAddAsync(beta));
        Assert.Equal("alpha", (await repository.FindAsync("tenant-a", alpha.Name))!.LatestActiveVersion!.Payload.Value);
        Assert.Equal("beta", (await repository.FindAsync("tenant-b", beta.Name))!.LatestActiveVersion!.Payload.Value);

        var current = await revisions.FindWithRevisionAsync("tenant-a", alpha.Name);
        Assert.NotNull(current);
        Assert.StartsWith("ef:", current.Revision, StringComparison.Ordinal);
        current.Secret.DisplayName = "updated";
        var updated = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Saved, updated.Status);
        Assert.NotEqual(current.Revision, updated.Revision);

        var stale = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Conflict, stale.Status);
        var missing = await revisions.SaveWithRevisionAsync(
            Secret("tenant-a", "missing", "value"),
            "ef:" + new string('0', 32));
        Assert.Equal(SecretRevisionSaveStatus.NotFound, missing.Status);
        var invalid = await revisions.SaveWithRevisionAsync(current.Secret, "gw:00000000000000000001");
        Assert.Equal(SecretRevisionSaveStatus.Conflict, invalid.Status);

        current.Secret.Status = SecretStatus.Deleted;
        await repository.SaveAsync(current.Secret);
        Assert.Equal(SecretStatus.Deleted, (await repository.FindAsync("tenant-a", alpha.Name))!.Status);
        var successor = Secret("tenant-a", alpha.Name, "successor");
        Assert.True(await repository.TryAddAsync(successor));
        Assert.Equal(successor.Id, (await repository.FindAsync("tenant-a", alpha.Name))!.Id);
    }

    [Fact]
    public async Task A_deleted_name_is_free_while_the_key_admits_one_row_holding_the_name_on_sqlite()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await using var bypass = await fixture.CreateSiblingAsync();
        await SecretNameReuseScenario.RunAsync(fixture.Context, bypass.Context);
    }

    [Fact]
    public async Task Saving_an_earlier_secrets_deleted_copy_leaves_its_successor_holding_the_name()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = fixture.Repository;
        var earlier = Secret("tenant-a", "payments.api", "earlier");
        await repository.SaveAsync(earlier);
        earlier.Status = SecretStatus.Deleted;
        await repository.SaveAsync(earlier);
        var successor = Secret("tenant-a", earlier.Name, "successor");
        Assert.True(await repository.TryAddAsync(successor));

        await repository.SaveAsync(earlier);

        var holder = (await repository.FindAsync("tenant-a", earlier.Name))!;
        Assert.Equal(successor.Id, holder.Id);
        Assert.Equal(SecretStatus.Active, holder.Status);
    }

    [Fact]
    public async Task List_combines_search_facets_status_scope_count_and_paging()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = fixture.Repository;
        await repository.SaveAsync(Secret("tenant-a", "payments.alpha", "a", "Payments Alpha", scope: "Finance"));
        await repository.SaveAsync(Secret("tenant-a", "payments.configuration", "b", "Payments Config", SecretStoreNames.Configuration, "Finance"));
        await repository.SaveAsync(Secret("tenant-a", "payments.other", "c", "Payments Other", scope: "Operations"));
        await repository.SaveAsync(Secret("tenant-a", "orders.alpha", "d", "Orders Alpha", scope: "Finance"));
        var deleted = Secret("tenant-a", "payments.deleted", "e", "Payments Deleted", scope: "Finance");
        deleted.Status = SecretStatus.Deleted;
        await repository.SaveAsync(deleted);

        var filtered = await repository.ListPageAsync("tenant-a", new SecretRepositoryListRequest(
            search: "PAYMENTS",
            typeName: SecretTypeNames.Text,
            typeNames: [SecretTypeNames.Text, SecretTypeNames.RsaKey],
            storeName: SecretStoreNames.Encrypted,
            storeNames: [SecretStoreNames.Encrypted],
            scope: "FINANCE",
            status: SecretStatus.Active,
            excludedStatus: SecretStatus.Deleted));
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal("payments.alpha", Assert.Single(filtered.Items).Name);

        var page = await repository.ListPageAsync("tenant-a", new SecretRepositoryListRequest(skip: 1, take: 2));
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(["payments.alpha", "payments.configuration"], page.Items.Select(secret => secret.Name));
    }

    [Fact]
    public async Task Lookup_facets_support_long_non_ascii_values_with_ordinal_ignore_case()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var typeName = new string('t', 100) + "é";
        var storeName = new string('s', 100) + "München";
        var scope = new string('p', 100) + "straße";
        var secret = Secret("tenant-a", "long.lookup", "value");
        secret.TypeName = typeName;
        secret.StoreName = storeName;
        secret.Scope = scope;

        await fixture.Repository.SaveAsync(secret);

        var page = await fixture.Repository.ListPageAsync(
            "tenant-a",
            new SecretRepositoryListRequest(
                typeName: typeName.ToUpperInvariant(),
                storeName: storeName.ToUpperInvariant(),
                scope: scope.ToUpperInvariant()));

        Assert.Equal(secret.Name, Assert.Single(page.Items).Name);
        var record = await fixture.Context.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal(SecretsSearchKeys.LookupKey(typeName), record.TypeNameLookupKey);
        Assert.Equal(SecretsSearchKeys.LookupKey(storeName), record.StoreNameLookupKey);
        Assert.Equal(SecretsSearchKeys.LookupKey(scope), record.ScopeLookupKey);
        Assert.All(
            new[] { record.TypeNameLookupKey, record.StoreNameLookupKey, record.ScopeLookupKey },
            key => Assert.True(key!.Length > 64));
    }

    [Fact]
    public async Task SaveWithRevision_concurrent_create_returns_conflict_for_the_loser()
    {
        var saveBarrier = new SaveBarrierInterceptor(2);
        await using var first = await SqliteFixture.CreateAsync(saveBarrier);
        await using var second = await first.CreateSiblingAsync();
        var revisions = new[]
        {
            Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(first.Repository),
            Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(second.Repository)
        };
        var secret = Secret("tenant-a", "racing.create", "value");

        var results = await Task.WhenAll(
            revisions.Select(repository => repository.SaveWithRevisionAsync(secret, expectedRevision: null).AsTask()));

        Assert.Contains(results, result => result.Status == SecretRevisionSaveStatus.Saved);
        Assert.Contains(results, result => result.Status == SecretRevisionSaveStatus.Conflict);
        Assert.Equal(1, await first.Context.Secrets.CountAsync());
    }

    [Fact]
    public async Task Save_concurrent_create_completes_both_calls_with_one_final_row()
    {
        var saveBarrier = new SaveBarrierInterceptor(2);
        await using var first = await SqliteFixture.CreateAsync(saveBarrier);
        await using var second = await first.CreateSiblingAsync();

        await Task.WhenAll(
            first.Repository.SaveAsync(Secret("tenant-a", "racing.unconditional-create", "first")).AsTask(),
            second.Repository.SaveAsync(Secret("tenant-a", "racing.unconditional-create", "second")).AsTask());

        Assert.Equal(1, await first.Context.Secrets.CountAsync());
        first.Context.ChangeTracker.Clear();
        var stored = await first.Repository.FindAsync("tenant-a", "racing.unconditional-create");
        Assert.Contains(stored!.LatestActiveVersion!.Payload.Value, new[] { "first", "second" });
    }

    [Fact]
    public async Task Save_is_unconditional_when_a_concurrent_writer_changes_the_tracked_row()
    {
        await using var first = await SqliteFixture.CreateAsync();
        await using var second = await first.CreateSiblingAsync();
        var seed = Secret("tenant-a", "racing.update", "seed");
        await first.Repository.SaveAsync(seed);

        // Load an intentionally stale tracked row in the second context before the first writer
        // advances the optimistic token. SaveAsync must refresh and apply the later writer.
        _ = await second.Context.Secrets.FindAsync([seed.TenantId, seed.Name, ""]);
        await first.Repository.SaveAsync(Secret("tenant-a", "racing.update", "first"));
        await second.Repository.SaveAsync(Secret("tenant-a", "racing.update", "second"));

        Assert.Equal(
            "second",
            (await first.Repository.FindAsync("tenant-a", "racing.update"))!.LatestActiveVersion!.Payload.Value);
    }

    [Fact]
    public async Task Save_clears_tracking_and_reports_bounded_concurrency_exhaustion()
    {
        var failures = new AlwaysFailConcurrencyInterceptor();
        await using var fixture = await SqliteFixture.CreateAsync(failures);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Repository.SaveAsync(Secret("tenant-a", "always.conflicts", "value")).AsTask());

        Assert.Equal(3, failures.Attempts);
        Assert.IsType<DbUpdateConcurrencyException>(exception.InnerException);
        Assert.Contains("after 3 attempts", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Active_only_uses_strict_expiry_across_every_active_version()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = fixture.Repository;
        await repository.SaveAsync(Secret("tenant-a", "a.non-expiring", "a"));
        await repository.SaveAsync(Secret("tenant-a", "b.future", "b", expiresAt: Now.AddMinutes(1)));
        await repository.SaveAsync(Secret("tenant-a", "c.boundary", "c", expiresAt: Now));
        await repository.SaveAsync(Secret("tenant-a", "d.expired", "d", expiresAt: Now.AddMinutes(-1)));
        await repository.SaveAsync(Secret(
            "tenant-a",
            "e.mixed",
            "e",
            versions:
            [
                Version("old", expiresAt: Now.AddMinutes(-1)),
                Version("future", expiresAt: Now.AddMinutes(1), version: 2)
            ]));

        var page = await repository.ListPageAsync("tenant-a", new SecretRepositoryListRequest(
            activeOnly: true,
            now: Now,
            take: 20));

        Assert.Equal(["a.non-expiring", "b.future", "e.mixed"], page.Items.Select(secret => secret.Name));
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task Active_only_normalizes_offset_expiries_to_utc()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        // 14:00+02:00 == 12:00Z; 10:30-01:00 == 11:30Z, so the version is still active.
        var plusTwo = new DateTimeOffset(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(2));
        await fixture.Repository.SaveAsync(Secret("tenant-a", "offset.future", "v", expiresAt: plusTwo));
        var now = new DateTimeOffset(2026, 8, 16, 10, 30, 0, TimeSpan.FromHours(-1));
        var page = await fixture.Repository.ListPageAsync(
            "tenant-a",
            new SecretRepositoryListRequest(activeOnly: true, now: now, take: 20));
        Assert.Equal("offset.future", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task Search_refuses_an_oversized_scoped_catalog()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        for (var index = 0; index < 10_001; index++)
            fixture.Context.Secrets.Add(SecretDocument.FromSecret(Secret("tenant-a", $"bounded-{index:D5}", "v")).ToRecord());
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Repository.ListPageAsync(
                "tenant-a",
                new SecretRepositoryListRequest(search: "pay", take: 25)));
        Assert.Contains("10,000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_table_is_module_scoped_after_migrate()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await using var command = fixture.Connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE '%EFMigrationsHistory%'";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        Assert.Contains(SecretsEfModule.HistoryTableName, names);
        Assert.DoesNotContain("__EFMigrationsHistory", names);
    }

    [Fact]
    public async Task Provider_guard_refuses_sqlite_context_when_postgres_is_expected()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EfDatabaseMigrator.ApplyAsync(fixture.Context, SecretsPostgreSqlDbContext.ExpectedProviderName));
        Assert.Contains(SecretsSqliteDbContext.ExpectedProviderName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_fails_when_the_initial_migration_is_pending()
    {
        await using var database = new TemporarySqliteDatabase("secrets-ef-pending");
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
            .UseSqlite(connection, sqlite => sqlite
                .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
            .Options;
        await using var context = new SecretsSqliteDbContext(options);
        var exception = await Assert.ThrowsAsync<EfPendingMigrationsException>(() =>
            EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName, EfMigratePolicy.Validate));
        Assert.Contains("pending migrations", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A provider failure an execution strategy re-raised must still reach the store's own exception, and must still
    /// leave the change tracker clean. Before #1814 the wrapper escaped this store untouched, because every clause
    /// here was keyed to <c>DbUpdateException</c> by type and the wrapper is an <c>InvalidOperationException</c>.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_wrapped_provider_failure_is_normalized_on_add_and_on_save(bool wrapped)
    {
        var failure = wrapped
            ? ProviderFailures.FailingSaveInterceptor.WrappedProviderFailure()
            : new ProviderFailures.FailingSaveInterceptor(() => new DbUpdateException("save failed", new ProviderFailures.SyntheticProviderException()));
        await using var fixture = await SqliteFixture.CreateAsync(failure);

        var add = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Repository.TryAddAsync(Secret("tenant-a", "payments.api", "alpha")).AsTask());

        Assert.Contains("Could not add secret", add.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());

        var save = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Repository.SaveAsync(Secret("tenant-a", "payments.api", "alpha")).AsTask());

        Assert.Contains("Could not save secret", save.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Wrapped, the old clause could not match a create race at all, so "another writer won" surfaced as a thrown
    /// exception instead of the documented <c>false</c> and <c>Conflict</c> answers.
    /// </summary>
    /// <remarks>
    /// The wrapper here is a store boundary's, not an execution strategy's, and the distinction is worth stating
    /// because an earlier version of this comment got it wrong. SQL Server's strategy wraps only errors its transient
    /// detector lists, and 2627 and 2601, the unique-key violations, are not among them; 1205 and 10054 are. The shape
    /// is identical either way, which is what this test pins, but the strategy is not what produces it for a conflict.
    /// </remarks>
    [Fact]
    public async Task A_wrapped_unique_violation_is_still_the_documented_conflict_answer()
    {
        const int uniqueViolation = 2627;
        var races = new ProviderFailures.FailingSaveInterceptor(
            () => ProviderFailures.WrappedSaveFailure(new ProviderFailures.SqlException(uniqueViolation)));
        await using var fixture = await SqliteFixture.CreateAsync(races);
        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(fixture.Repository);

        Assert.False(await fixture.Repository.TryAddAsync(Secret("tenant-a", "payments.api", "alpha")));
        Assert.Empty(fixture.Context.ChangeTracker.Entries());

        var saved = await revisions.SaveWithRevisionAsync(Secret("tenant-a", "payments.api", "beta"), null);

        Assert.Equal(SecretRevisionSaveStatus.Conflict, saved.Status);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task A_row_with_a_newer_incompatible_schema_reports_skew_on_every_read_and_before_a_write_replaces_it()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(fixture.Repository);
        var secret = Secret("tenant-a", "payments.api", "alpha");
        await fixture.Repository.SaveAsync(secret);
        var revision = (await revisions.FindWithRevisionAsync("tenant-a", secret.Name))!.Revision;
        var row = await fixture.Context.Secrets.SingleAsync();
        Assert.Equal(SecretsEfModule.SchemaVersion, row.SchemaVersion);

        await EfSchemaVersionSkewTestSupport.ArrangeSkewedRowAsync(fixture.Context, v => row.SchemaVersion = v, v => row.Payload = v);

        Func<Task>[] operations =
        [
            () => fixture.Repository.FindAsync("tenant-a", secret.Name).AsTask(),
            () => revisions.FindWithRevisionAsync("tenant-a", secret.Name).AsTask(),
            () => fixture.Repository.ListPageAsync("tenant-a", new SecretRepositoryListRequest()).AsTask(),
            () => fixture.Repository.SaveAsync(Secret("tenant-a", secret.Name, "beta")).AsTask(),
            () => revisions.SaveWithRevisionAsync(Secret("tenant-a", secret.Name, "beta"), revision).AsTask(),
            () => SecretsProjectionContract.HasLegacyProjectionsAsync(fixture.Context)
        ];
        foreach (var operation in operations)
        {
            var skew = await Assert.ThrowsAsync<EfSchemaVersionSkewException>(operation);
            EfSchemaVersionSkewTestSupport.AssertSchemaVersionSkew(skew, "Secrets", SecretsEfModule.SchemaVersion);
            fixture.Context.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Secrets keeps a migration chain, so a database can hold rows written before the stamp column existed. Those
    /// builds wrote the 1.0.0 content format, and the migration that adds the column stamps the rows it finds with
    /// it, so a secret saved before the upgrade reads the same after it.
    /// </summary>
    [Fact]
    public async Task A_row_written_before_the_stamp_existed_carries_the_version_it_was_written_in_after_the_stamp_migration()
    {
        await using var database = await MigrateToJustBeforeAsync("_SchemaVersionStamp");
        await using (var preStamp = new SecretsSqliteDbContext(PreStampSecretsModel.Use(SqliteOptions(database.Connection)).Options))
            await PreStampSecretsModel.WriteAsync(preStamp, Secret("tenant-a", "payments.api", "alpha"));

        await database.Migrator.MigrateAsync();

        var context = database.Context;
        Assert.Equal(SecretsEfModule.SchemaVersion, (await context.Secrets.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal("alpha", (await new EfSecretRepository(context).FindAsync("tenant-a", "payments.api"))!.LatestActiveVersion!.Payload.Value);
        Assert.False(await new SecretsProjectionReindex().AuditAsync(context));
    }

    /// <summary>
    /// Rows written under the old key, one per name whatever their status, upgrade into the key that frees a deleted
    /// secret's name: the live row keeps its name, the deleted row moves aside and keeps its history, the deleted name
    /// takes a new secret, and the startup audit, which now pages by the whole key, reads every row as current.
    /// </summary>
    [Fact]
    public async Task Rows_written_before_names_could_be_reused_upgrade_with_deleted_names_freed()
    {
        await using var database = await MigrateToJustBeforeAsync("_SchemaVersionStamp");
        var deletedBeforeUpgrade = Secret("tenant-a", "deleted.before.upgrade", "deleted-value");
        deletedBeforeUpgrade.Status = SecretStatus.Deleted;
        await using (var preStamp = new SecretsSqliteDbContext(PreStampSecretsModel.Use(SqliteOptions(database.Connection)).Options))
        {
            await PreStampSecretsModel.WriteAsync(preStamp, Secret("tenant-a", "live.before.upgrade", "live-value"));
            await PreStampSecretsModel.WriteAsync(preStamp, deletedBeforeUpgrade);
        }

        await database.Migrator.MigrateAsync();

        var context = database.Context;
        var rows = await context.Secrets.AsNoTracking().OrderBy(row => row.NormalizedName).ToListAsync();
        Assert.Equal(["deleted.before.upgrade", "live.before.upgrade"], rows.Select(row => row.NormalizedName));
        Assert.NotEqual("", rows[0].Tombstone);
        Assert.Equal("", rows[1].Tombstone);
        Assert.False(await new SecretsProjectionReindex().AuditAsync(context));

        var repository = new EfSecretRepository(context);
        Assert.Equal("live-value", (await repository.FindAsync("tenant-a", "live.before.upgrade"))!.LatestActiveVersion!.Payload.Value);
        Assert.False(await repository.TryAddAsync(Secret("tenant-a", "live.before.upgrade", "duplicate")));
        var successor = Secret("tenant-a", deletedBeforeUpgrade.Name, "successor-value");
        Assert.True(await repository.TryAddAsync(successor));
        Assert.Equal(successor.Id, (await repository.FindAsync("tenant-a", deletedBeforeUpgrade.Name))!.Id);
        var history = Assert.Single((await repository.ListPageAsync(
            "tenant-a",
            new SecretRepositoryListRequest(status: SecretStatus.Deleted))).Items);
        Assert.Equal(deletedBeforeUpgrade.Id, history.Id);
        Assert.Equal("deleted-value", Assert.Single(history.Versions).Payload.Value);
    }

    /// <summary>
    /// A host still on a build from before <c>FreeNameOnDelete</c> knows no tombstone, so a delete it makes after the
    /// migration has been applied updates the row in place and leaves the deleted secret holding its name. The audit
    /// reports the declared reindex as required, the one answer <c>post-migrate</c> acts on, rather than calling the
    /// database current or failing it as damaged, and the reindex moves the row under the secret's id: the name is free
    /// and the deleted secret keeps its history.
    /// </summary>
    [Fact]
    public async Task A_secret_an_older_host_deleted_in_place_requires_the_reindex_which_frees_its_name()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var secret = Secret("tenant-a", "deleted.in.place", "kept-value");
        Assert.True(await fixture.Repository.TryAddAsync(secret));
        await using (var olderHost = new SecretsSqliteDbContext(SqliteOptions(fixture.Connection)
                         .ReplaceService<IModelCustomizer, PreReuseSecretsModel>()
                         .Options))
        {
            var row = await olderHost.Secrets.SingleAsync();
            secret.Status = SecretStatus.Deleted;
            SecretDocument.FromSecret(secret).CopyProjectionsTo(row);
            await olderHost.SaveChangesAsync();
        }

        var deletedInPlace = await fixture.Context.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal(("", "deleted"), (deletedInPlace.Tombstone, deletedInPlace.Status));
        Assert.False(await fixture.Repository.TryAddAsync(Secret("tenant-a", secret.Name, "refused-value")));
        var startup = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SecretsProjectionContract.EnsureCurrentAsync(fixture.Context));
        Assert.IsNotType<SecretsProjectionException>(startup);
        Assert.Contains(EfPostMigrationActions.Command, startup.Message, StringComparison.Ordinal);
        var reindex = new SecretsProjectionReindex();
        Assert.True(await reindex.AuditAsync(fixture.Context));

        await reindex.RunAsync(fixture.Context);

        Assert.False(await reindex.AuditAsync(fixture.Context));
        await SecretsProjectionContract.EnsureCurrentAsync(fixture.Context);
        var moved = await fixture.Context.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal(secret.Id, moved.Tombstone);
        Assert.Equal(deletedInPlace.ConcurrencyToken, moved.ConcurrencyToken);
        var successor = Secret("tenant-a", secret.Name, "successor-value");
        Assert.True(await fixture.Repository.TryAddAsync(successor));
        Assert.Equal(successor.Id, (await fixture.Repository.FindAsync("tenant-a", secret.Name))!.Id);
        var history = Assert.Single((await fixture.Repository.ListPageAsync(
            "tenant-a",
            new SecretRepositoryListRequest(status: SecretStatus.Deleted))).Items);
        Assert.Equal(secret.Id, history.Id);
        Assert.Equal("kept-value", Assert.Single(history.Versions).Payload.Value);
    }

    /// <summary>
    /// Deleting a secret writes a row keyed by its tenant id, name and id, so a secret is accepted only when that row fits
    /// <see cref="SecretNameConstraints.MaximumKeyLength"/>, on every provider: one at the limit is created and deleted,
    /// and one past it is refused by every write that would add it.
    /// </summary>
    [Fact]
    public async Task A_secret_is_accepted_only_when_the_row_its_delete_writes_fits_the_key()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(fixture.Repository);
        var tenantId = new string('t', 200);
        var atLimit = Secret(tenantId, new string('a', SecretNameConstraints.MaximumKeyLength - tenantId.Length - 11), "value");
        Assert.Equal(SecretNameConstraints.MaximumKeyLength, tenantId.Length + atLimit.Name.Length + atLimit.Id.Length);

        Assert.True(await fixture.Repository.TryAddAsync(atLimit));
        var loaded = (await revisions.FindWithRevisionAsync(tenantId, atLimit.Name))!;
        loaded.Secret.Status = SecretStatus.Deleted;
        Assert.Equal(SecretRevisionSaveStatus.Saved, (await revisions.SaveWithRevisionAsync(loaded.Secret, loaded.Revision)).Status);
        Assert.Equal(atLimit.Id, (await fixture.Context.Secrets.AsNoTracking().SingleAsync()).Tombstone);

        var pastLimit = Secret(tenantId, atLimit.Name + "a", "value");
        Func<Task>[] writes =
        [
            () => fixture.Repository.TryAddAsync(pastLimit).AsTask(),
            () => fixture.Repository.SaveAsync(pastLimit).AsTask(),
            () => revisions.SaveWithRevisionAsync(pastLimit, null).AsTask()
        ];
        foreach (var write in writes)
        {
            var refused = await Assert.ThrowsAsync<ArgumentException>(write);
            Assert.Contains($"cannot exceed {atLimit.Name.Length} characters", refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(1, await fixture.Context.Secrets.CountAsync());
    }

    /// <summary>
    /// The stamp migration fills the column only for the rows it finds, then drops the default: a row written later
    /// without a stamp is refused, never read as the baseline.
    /// </summary>
    [Fact]
    public async Task A_row_written_without_a_stamp_after_the_stamp_migration_is_refused()
    {
        await using var database = new TemporarySqliteDatabase("secrets-ef-poststamp");
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var context = new SecretsSqliteDbContext(SqliteOptions(connection).Options);
        await context.Database.MigrateAsync();
        await using var preStamp = new SecretsSqliteDbContext(SqliteOptions(connection)
            .ReplaceService<IModelCustomizer, UnstampedSecretsModel>()
            .Options);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new EfSecretRepository(preStamp).SaveAsync(Secret("tenant-a", "payments.api", "alpha")).AsTask());

        var constraint = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(failure.InnerException).InnerException);
        Assert.Contains($"{SecretsEfModule.TableName}.SchemaVersion", constraint.Message, StringComparison.Ordinal);
    }

    private static DbContextOptionsBuilder<SecretsSqliteDbContext> SqliteOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<SecretsSqliteDbContext>()
            .UseSqlite(connection, sqlite => sqlite
                .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName));

    /// <summary>
    /// A database migrated up to the migration just before the one whose name ends with <paramref name="migrationSuffix"/>,
    /// for a test that writes rows the way builds from before that migration did and then applies the rest.
    /// </summary>
    private static async Task<PartlyMigratedDatabase> MigrateToJustBeforeAsync(string migrationSuffix)
    {
        var database = new PartlyMigratedDatabase();
        try
        {
            await database.Connection.OpenAsync();
            var migrations = database.Context.Database.GetMigrations().ToArray();
            var migration = Assert.Single(migrations, candidate => candidate.EndsWith(migrationSuffix, StringComparison.Ordinal));
            await database.Migrator.MigrateAsync(migrations[Array.IndexOf(migrations, migration) - 1]);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static Secret Secret(
        string tenantId,
        string name,
        string value,
        string? displayName = null,
        string storeName = SecretStoreNames.Encrypted,
        string? scope = null,
        DateTimeOffset? expiresAt = null,
        IList<SecretVersion>? versions = null) => new()
        {
            TenantId = tenantId,
            Name = name,
            DisplayName = displayName ?? name,
            TypeName = SecretTypeNames.Text,
            StoreName = storeName,
            Scope = scope,
            Versions = versions ?? [Version(value, expiresAt)]
        };

    private static SecretVersion Version(string value, DateTimeOffset? expiresAt = null, int version = 1) => new()
    {
        Version = version,
        Status = SecretStatus.Active,
        ExpiresAt = expiresAt,
        Payload = SecretPayload.FromValue(value)
    };

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private SqliteFixture(
            string path,
            SqliteConnection connection,
            SecretsSqliteDbContext context,
            ISecretRepository repository,
            IInterceptor? interceptor,
            TemporarySqliteDatabase? ownedDatabase = null)
        {
            Path = path;
            Connection = connection;
            Context = context;
            Repository = repository;
            _interceptor = interceptor;
            _ownedDatabase = ownedDatabase;
        }

        private string Path { get; }
        public SqliteConnection Connection { get; }
        public SecretsSqliteDbContext Context { get; }
        public ISecretRepository Repository { get; }

        public async ValueTask<SqliteFixture> CreateSiblingAsync()
        {
            var connection = new SqliteConnection($"Data Source={Path}");
            try
            {
                await connection.OpenAsync();
                var options = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
                    .UseSqlite(connection, sqlite => sqlite
                        .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                        .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
                    .AddInterceptors(_interceptor is null ? [] : [_interceptor])
                    .Options;
                var context = new SecretsSqliteDbContext(options);
                return new SqliteFixture(Path, connection, context, new EfSecretRepository(context), _interceptor);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        public static async ValueTask<SqliteFixture> CreateAsync(IInterceptor? interceptor = null)
        {
            var database = new TemporarySqliteDatabase("secrets-ef");
            var connection = new SqliteConnection(database.ConnectionString);
            try
            {
                await connection.OpenAsync();
                var options = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
                    .UseSqlite(connection, sqlite => sqlite
                        .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                        .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
                    .AddInterceptors(interceptor is null ? [] : [interceptor])
                    .Options;
                var context = new SecretsSqliteDbContext(options);
                try
                {
                    await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);
                    return new SqliteFixture(database.Path, connection, context, new EfSecretRepository(context), interceptor, database);
                }
                catch (Exception)
                {
                    await context.DisposeAsync();
                    throw;
                }
            }
            catch (Exception)
            {
                await connection.DisposeAsync();
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
            if (_ownedDatabase is not null)
                await _ownedDatabase.DisposeAsync();
        }

        private readonly TemporarySqliteDatabase? _ownedDatabase;
        private readonly IInterceptor? _interceptor;
    }

    private sealed class PartlyMigratedDatabase : IAsyncDisposable
    {
        private readonly TemporarySqliteDatabase _database = new("secrets-ef-upgrade");

        public PartlyMigratedDatabase()
        {
            Connection = new SqliteConnection(_database.ConnectionString);
            Context = new SecretsSqliteDbContext(SqliteOptions(Connection).Options);
        }

        public SqliteConnection Connection { get; }
        public SecretsSqliteDbContext Context { get; }
        public IMigrator Migrator => Context.GetService<IMigrator>();

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    private sealed class SaveBarrierInterceptor(int participantCount) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == participantCount)
                _release.TrySetResult();

            await _release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    /// <summary>The current model without the stamp column, so the only thing a write with it leaves out is the stamp.</summary>
    private sealed class UnstampedSecretsModel(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<SecretRecord>().Ignore(record => record.SchemaVersion);
        }
    }

    private sealed class AlwaysFailConcurrencyInterceptor : SaveChangesInterceptor
    {
        private int _attempts;

        public int Attempts => _attempts;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            throw new DbUpdateConcurrencyException("Forced test conflict.");
        }
    }
}
