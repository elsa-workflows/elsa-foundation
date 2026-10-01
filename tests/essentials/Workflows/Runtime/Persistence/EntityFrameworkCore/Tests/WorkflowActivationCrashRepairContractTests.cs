using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Runs <see cref="WorkflowActivationCrashRepairContract"/> against the in-memory stores and the EF Core stores on SQLite,
/// and its concurrent completions that SQLite can interleave on SQLite alone. The provider tests run the same contract on
/// PostgreSQL.
/// </summary>
public sealed class WorkflowActivationCrashRepairContractTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase _database = new("activation-crash");

    public static TheoryData<string> Scenarios => WorkflowActivationCrashRepairContract.Scenarios;

    public static TheoryData<string> ConcurrentCompletionScenarios => WorkflowActivationCrashRepairContract.ConcurrentCompletionScenarios;

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

    [Theory]
    [MemberData(nameof(ConcurrentCompletionScenarios))]
    public async Task Sqlite_concurrent_completions(string scenario)
    {
        await using (var context = OpenContext())
            await context.Database.EnsureCreatedAsync();

        await WorkflowActivationCrashRepairContract.RunConcurrentAsync(scenario, interceptors => ActivationStores.EntityFramework(OpenContext(interceptors), "tenant-a"));
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private RuntimeSqliteDbContext OpenContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(_database.ConnectionString).AddInterceptors(interceptors).Options);
}
