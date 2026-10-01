using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>The live-drain and sweep contention contract on the EF Runtime stores over a SQLite file.</summary>
public sealed class RuntimeEntityFrameworkCoreLiveDrainSweepContentionTests : LiveDrainSweepContentionContractTests
{
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-runtime-ef-contention-{Guid.NewGuid():N}.db");

    protected override void ConfigureStore(IServiceCollection services) =>
        services
            .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={_databasePath};Pooling=False",
                RecoveryContinuationSigningKey = "ef-runtime-contention-recovery-signing-key-32",
                HierarchyCursorSigningKey = "ef-runtime-contention-hierarchy-signing-key-32"
            })
            .AddEfModuleMigrations<RuntimeDbContext>("Sqlite");

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(file);
    }
}
