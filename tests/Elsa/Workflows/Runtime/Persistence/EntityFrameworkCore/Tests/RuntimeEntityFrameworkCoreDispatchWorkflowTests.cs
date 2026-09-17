using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>The waited-child store contract on the EF Runtime stores over SQLite.</summary>
public sealed class RuntimeEntityFrameworkCoreDispatchWorkflowTests : DispatchWorkflowStoreContractTests
{
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-runtime-ef-dispatch-{Guid.NewGuid():N}.db");

    protected override void ConfigureStore(IServiceCollection services) =>
        services
            .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={_databasePath};Pooling=False",
                RecoveryContinuationSigningKey = "ef-runtime-dispatch-recovery-signing-key-32",
                HierarchyCursorSigningKey = "ef-runtime-dispatch-hierarchy-signing-key-32"
            })
            .AddEfModuleMigrations<BookmarkStateDbContext>("Sqlite");

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(file);
    }
}
