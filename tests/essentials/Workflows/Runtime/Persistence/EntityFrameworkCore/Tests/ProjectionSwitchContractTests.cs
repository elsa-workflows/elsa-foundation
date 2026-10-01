using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Runs <see cref="ProjectionSwitchContract"/> on SQLite. The provider tests run it on PostgreSQL, with the scenarios that
/// interleave another writer inside a switch's transaction, which SQLite's write lock rules out.
/// </summary>
public sealed class ProjectionSwitchContractTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase _database = new("projection-switch");

    public static TheoryData<string, string> Scenarios => ProjectionSwitchContract.Scenarios;

    [Theory]
    [MemberData(nameof(Scenarios))]
    public Task Sqlite(string store, string scenario) =>
        ProjectionSwitchContract.RunAsync(store, scenario, interceptors => new RuntimeSqliteDbContext(
            new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(_database.ConnectionString).AddInterceptors(interceptors).Options), "tenant-a");

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
