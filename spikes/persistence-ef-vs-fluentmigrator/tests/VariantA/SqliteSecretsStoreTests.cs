using Elsa.Persistence.Spike.VariantA;
using Elsa.Persistence.Spike.VariantA.Tooling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.Spike.VariantA.Tests;

public sealed class SqliteSecretsStoreTests
{
    [Fact]
    public async Task Migrate_then_add_and_get_by_name()
    {
        using var db = TempSqlite.Create();
        await using var context = VariantAContextFactory.CreateSqlite(db.ConnectionString);
        await VariantAMigrations.ApplyAsync(context, ProviderGuard.SqliteProviderName);

        ISecretStore store = new EfSecretStore<SecretsSqliteDbContext>(context);
        var secret = new SecretRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-a",
            Name = "smtp-password",
            Payload = """{"kind":"text","value":"s3cret"}"""
        };

        await store.AddAsync(secret);
        var loaded = await store.GetByNameAsync("tenant-a", "smtp-password");

        Assert.NotNull(loaded);
        Assert.Equal(secret.Id, loaded.Id);
        Assert.Equal(secret.Payload, loaded.Payload);
        Assert.NotEmpty(loaded.RowVersion);
    }

    [Fact]
    public async Task History_table_is_module_scoped()
    {
        using var db = TempSqlite.Create();
        await using var context = VariantAContextFactory.CreateSqlite(db.ConnectionString);
        await VariantAMigrations.ApplyAsync(context, ProviderGuard.SqliteProviderName);

        var tables = await context.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name")
            .ToListAsync();

        Assert.Contains(SchemaNames.Table, tables);
        Assert.Contains(SchemaNames.EfHistoryTable, tables);
        Assert.DoesNotContain("__EFMigrationsHistory", tables);
    }

    [Fact]
    public async Task Provider_guard_rejects_the_wrong_dialect()
    {
        using var db = TempSqlite.Create();
        await using var context = VariantAContextFactory.CreateSqlite(db.ConnectionString);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => VariantAMigrations.ApplyAsync(context, ProviderGuard.NpgsqlProviderName));

        Assert.Contains("Expected", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_update_throws_db_update_concurrency()
    {
        using var db = TempSqlite.Create();
        await using var context = VariantAContextFactory.CreateSqlite(db.ConnectionString);
        await VariantAMigrations.ApplyAsync(context, ProviderGuard.SqliteProviderName);

        var id = Guid.NewGuid();
        context.Secrets.Add(new SecretRecord
        {
            Id = id,
            TenantId = "t",
            Name = "occ",
            Payload = "{}"
        });
        await context.SaveChangesAsync();

        await using var stale = VariantAContextFactory.CreateSqlite(db.ConnectionString);
        var first = await stale.Secrets.SingleAsync(x => x.Id == id);

        var live = await context.Secrets.SingleAsync(x => x.Id == id);
        live.Payload = """{"v":1}""";
        await context.SaveChangesAsync();

        first.Payload = """{"v":2}""";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }
}

internal sealed class TempSqlite : IDisposable
{
    private TempSqlite(string path) => Path = path;

    public string Path { get; }
    public string ConnectionString => $"Data Source={Path}";

    public static TempSqlite Create() =>
        new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"elsa-spike-a-{Guid.NewGuid():N}.sqlite"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(Path))
            File.Delete(Path);
    }
}
