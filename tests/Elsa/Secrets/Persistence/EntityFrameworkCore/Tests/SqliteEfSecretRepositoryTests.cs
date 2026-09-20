using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        Assert.False(await repository.TryAddAsync(Secret("tenant-a", alpha.Name, "reserved")));
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
        _ = await second.Context.Secrets.FindAsync([seed.TenantId, seed.Name]);
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
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
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
    /// The create race is the case that mattered most: wrapped, the old clause could not match it at all, so "another
    /// writer won" surfaced as a thrown exception instead of the documented <c>false</c> and <c>Conflict</c> answers.
    /// </summary>
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
