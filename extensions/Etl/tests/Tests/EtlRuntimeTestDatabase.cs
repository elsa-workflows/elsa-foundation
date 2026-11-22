using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Etl.Tests;

/// <summary>A fresh SQLite runtime database file, migrated through the retained SQLite chain.</summary>
internal sealed class EtlRuntimeTestDatabase(string directory) : IEtlRuntimeTestDatabase
{
    private readonly string _connectionString = $"Data Source={Path.Combine(directory, "etl-runtime.db")};Pooling=False";

    public static async Task<EtlRuntimeTestDatabase> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = new EtlRuntimeTestDatabase(directory);
        await using var context = database.CreateContext();
        await EfDatabaseMigrator.ApplyAsync(context, EtlRuntimeSqliteDbContext.ExpectedProviderName);
        return database;
    }

    public EtlRuntimeSqliteDbContext CreateContext(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<EtlRuntimeSqliteDbContext>()
        .UseSqlite(_connectionString, sqlite => sqlite
            .MigrationsAssembly(typeof(EtlRuntimeSqliteDbContext).Assembly.GetName().Name)
            .MigrationsHistoryTable(EtlRuntimeEfModule.HistoryTableName))
        .AddInterceptors(interceptors)
        .Options);

    EtlRuntimeDbContext IEtlRuntimeTestDatabase.CreateContext(params IInterceptor[] interceptors) => CreateContext(interceptors);

    public EfEtlRuntimeStore CreateStore(EtlRuntimeDbContext context, TimeProvider? clock = null,
        TimeSpan? preparationLease = null, TimeSpan? partitionLease = null) =>
        EtlRuntimeTestStores.Create(context, clock, preparationLease, partitionLease);

    public ValueTask DisposeAsync()
    {
        Directory.Delete(directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}
