using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Runs <see cref="WorkflowActivationCrashRepairContract"/> against the in-memory stores and the EF Core stores on SQLite.
/// The provider tests run the same contract on PostgreSQL, with the switches interleaved inside their transactions that
/// SQLite cannot interleave.
/// </summary>
public sealed class WorkflowActivationCrashRepairContractTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase _database = new("activation-crash");

    public static TheoryData<string> Scenarios => WorkflowActivationCrashRepairContract.Scenarios;

    [Theory]
    [MemberData(nameof(Scenarios))]
    public Task In_memory(string scenario)
    {
        var stores = ActivationStores.InMemory();
        return WorkflowActivationCrashRepairContract.RunAsync(scenario, () => stores);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Sqlite(string scenario)
    {
        await using (var context = OpenContext())
            await context.Database.EnsureCreatedAsync();

        await WorkflowActivationCrashRepairContract.RunAsync(scenario, () => ActivationStores.EntityFramework(OpenContext(), "tenant-a"));
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private RuntimeSqliteDbContext OpenContext() =>
        new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(_database.ConnectionString).Options);
}
