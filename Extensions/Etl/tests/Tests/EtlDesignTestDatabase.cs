using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

internal sealed class EtlDesignTestDatabase(string directory) : IAsyncDisposable
{
    private readonly string _connectionString = $"Data Source={Path.Combine(directory, "etl.db")};Pooling=False";

    public static async Task<EtlDesignTestDatabase> CreateAsync(string? retainedDatabase = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (retainedDatabase is not null)
            File.Copy(retainedDatabase, Path.Combine(directory, "etl.db"));
        var database = new EtlDesignTestDatabase(directory);
        await using var context = database.CreateContext();
        await EfDatabaseMigrator.ApplyAsync(context, EtlDesignSqliteDbContext.ExpectedProviderName);
        return database;
    }

    public EtlDesignSqliteDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EtlDesignSqliteDbContext>()
            .UseSqlite(_connectionString, sqlite => sqlite
                .MigrationsAssembly(typeof(EtlDesignSqliteDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(EtlDesignEfModule.HistoryTableName))
            .Options;
        return new EtlDesignSqliteDbContext(options);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}
