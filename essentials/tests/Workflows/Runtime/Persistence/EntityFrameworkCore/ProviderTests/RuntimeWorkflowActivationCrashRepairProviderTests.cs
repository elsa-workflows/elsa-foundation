using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// Runs <see cref="WorkflowActivationCrashRepairContract"/> on PostgreSQL, including the switches and discards that
/// another writer interleaves inside their read committed transactions (#2230). Each scenario gets its own persistence
/// scope.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeWorkflowActivationCrashRepairPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    public static TheoryData<string> Scenarios => WorkflowActivationCrashRepairContract.Scenarios;

    public static TheoryData<string> ConcurrentSwitchScenarios => WorkflowActivationCrashRepairContract.ConcurrentSwitchScenarios;

    [SkippableTheory]
    [MemberData(nameof(Scenarios))]
    public async Task PostgreSql(string scenario)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        await using (var context = OpenContext())
            await context.Database.EnsureCreatedAsync();

        var scope = $"activation-crash-{Guid.NewGuid():N}";
        await WorkflowActivationCrashRepairContract.RunAsync(scenario, () => ActivationStores.EntityFramework(OpenContext(), scope));
    }

    [SkippableTheory]
    [MemberData(nameof(ConcurrentSwitchScenarios))]
    public async Task PostgreSql_concurrent_switches(string scenario)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        await using (var context = OpenContext())
            await context.Database.EnsureCreatedAsync();

        var scope = $"activation-concurrent-{Guid.NewGuid():N}";
        await WorkflowActivationCrashRepairContract.RunConcurrentAsync(scenario, interceptors => ActivationStores.EntityFramework(OpenContext(interceptors), scope));
    }

    private RuntimePostgreSqlDbContext OpenContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(fixture.ConnectionString).AddInterceptors(interceptors).Options);
}
