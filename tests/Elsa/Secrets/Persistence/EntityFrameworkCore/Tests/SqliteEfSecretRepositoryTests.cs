using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        var plusTwo = new DateTimeOffset(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(2));
        await fixture.Repository.SaveAsync(Secret("tenant-a", "offset.future", "v", expiresAt: plusTwo));
        var now = new DateTimeOffset(2026, 8, 16, 11, 30, 0, TimeSpan.FromHours(-1));
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
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-ef-pending-{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        try
        {
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
        finally
        {
            File.Delete(path);
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
        private SqliteFixture(string path, SqliteConnection connection, SecretsSqliteDbContext context, ISecretRepository repository)
        {
            Path = path;
            Connection = connection;
            Context = context;
            Repository = repository;
        }

        private string Path { get; }
        public SqliteConnection Connection { get; }
        public SecretsSqliteDbContext Context { get; }
        public ISecretRepository Repository { get; }

        public static async ValueTask<SqliteFixture> CreateAsync()
        {
            var path = System.IO.Path.Join(System.IO.Path.GetTempPath(), $"elsa-secrets-ef-{Guid.NewGuid():N}.db");
            var connection = new SqliteConnection($"Data Source={path}");
            try
            {
                await connection.OpenAsync();
                var options = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
                    .UseSqlite(connection, sqlite => sqlite
                        .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                        .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
                    .Options;
                var context = new SecretsSqliteDbContext(options);
                try
                {
                    await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);
                    return new SqliteFixture(path, connection, context, new EfSecretRepository(context));
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
                File.Delete(path);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
            File.Delete(Path);
        }
    }
}
