using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>
/// A temporary SQLite file holding the import ledger and both Design lanes. The harness owns that file:
/// disposing it disposes the database and deletes the file. An <see cref="ImportDatabase"/> opened directly
/// with <see cref="ImportDatabase.Sqlite"/> owns only its contexts.
/// </summary>
internal sealed class SqliteImportHarness : IAsyncDisposable
{
    private readonly string path;

    private SqliteImportHarness(string path)
    {
        this.path = path;
        ConnectionString = ConnectionStringFor(path);
        Database = ImportDatabase.Sqlite(ConnectionString);
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

    public static DbContextOptions<TContext> Options<TContext>(string connectionString, params IInterceptor[] interceptors)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseSqlite(connectionString).AddInterceptors(interceptors).Options;

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        TemporarySqliteDatabase.ClearPoolAndDeleteFiles(path);
    }
}
