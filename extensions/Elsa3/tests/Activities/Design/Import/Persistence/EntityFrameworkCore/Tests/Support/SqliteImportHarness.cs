using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>A temporary SQLite file holding the import ledger and both Design lanes.</summary>
internal sealed class SqliteImportHarness : IAsyncDisposable
{
    private readonly string path;

    private SqliteImportHarness(string path)
    {
        this.path = path;
        ConnectionString = ConnectionStringFor(path);
        Database = For(ConnectionString);
    }

    public string ConnectionString { get; }

    public ImportDatabase Database { get; }

    public static async Task<SqliteImportHarness> CreateAsync()
    {
        var harness = new SqliteImportHarness(Path.Combine(Path.GetTempPath(), $"elsa3-import-ef-{Guid.NewGuid():N}.db"));
        await harness.Database.CreateSchemaAsync();
        return harness;
    }

    public static string ConnectionStringFor(string path) => $"Data Source={path}";

    public static ImportDatabase For(string connectionString) => new(
        interceptors => new Elsa3ImportSqliteDbContext(Options<Elsa3ImportSqliteDbContext>(connectionString, interceptors)),
        interceptors => new ActivitiesDesignSqliteDbContext(Options<ActivitiesDesignSqliteDbContext>(connectionString, interceptors)),
        interceptors => new WorkflowsDesignSqliteDbContext(Options<WorkflowsDesignSqliteDbContext>(connectionString, interceptors)));

    public static DbContextOptions<TContext> Options<TContext>(string connectionString, params IInterceptor[] interceptors)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseSqlite(connectionString).AddInterceptors(interceptors).Options;

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            if (File.Exists(path + suffix))
                File.Delete(path + suffix);
        }
    }
}
