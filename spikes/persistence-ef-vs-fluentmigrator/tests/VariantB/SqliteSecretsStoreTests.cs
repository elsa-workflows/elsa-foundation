using Elsa.Persistence.Spike.VariantB;
using Elsa.Persistence.Spike.VariantB.Host;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Persistence.Spike.VariantB.Tests;

public sealed class SqliteSecretsStoreTests
{
    [Fact]
    public async Task FluentMigrator_apply_then_ef_crud()
    {
        using var db = TempSqlite.Create();
        FluentMigratorApply.Apply(db.ConnectionString, SpikeSqlProvider.Sqlite);

        await using var context = VariantBContextFactory.CreateSqlite(db.ConnectionString);
        ISecretStore store = new EfSecretStore(context);
        var secret = new SecretRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-b",
            Name = "api-key",
            Payload = """{"kind":"text","value":"k"}"""
        };

        await store.AddAsync(secret);
        var loaded = await store.GetByNameAsync("tenant-b", "api-key");
        Assert.NotNull(loaded);
        Assert.Equal(secret.Id, loaded.Id);
    }

    [Fact]
    public void Version_table_is_module_scoped()
    {
        using var db = TempSqlite.Create();
        FluentMigratorApply.Apply(db.ConnectionString, SpikeSqlProvider.Sqlite);
        using var connection = new SqliteConnection(db.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
            tables.Add(reader.GetString(0));

        Assert.Contains(SchemaNames.Table, tables);
        Assert.Contains(SchemaNames.FluentMigratorVersionTable, tables);
        Assert.DoesNotContain("VersionInfo", tables);
    }

    [Fact]
    public void Provider_guard_rejects_sqlite_file_on_postgres_runner()
    {
        using var db = TempSqlite.Create();
        Assert.ThrowsAny<Exception>(
            () => FluentMigratorApply.Apply(db.ConnectionString, SpikeSqlProvider.PostgreSql));
    }

    [Fact]
    public void After_fm_apply_ef_model_differ_reports_drift_status()
    {
        using var db = TempSqlite.Create();
        FluentMigratorApply.Apply(db.ConnectionString, SpikeSqlProvider.Sqlite);
        using var context = VariantBContextFactory.CreateSqlite(db.ConnectionString);
        var differences = EfModelDrift.GetDifferences(context);

        // APIs: IMigrationsModelDiffer.GetDifferences(source: null, target: IDesignTimeModel.Model.GetRelationalModel())
        // plus IDatabaseModelFactory.Create on the live connection. Empty means FM created every EF table/column.
        Assert.Empty(differences);
    }
}

internal sealed class TempSqlite : IDisposable
{
    private TempSqlite(string path) => Path = path;

    public string Path { get; }
    public string ConnectionString => $"Data Source={Path}";

    public static TempSqlite Create() =>
        new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"elsa-spike-b-{Guid.NewGuid():N}.sqlite"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(Path))
            File.Delete(Path);
    }
}
