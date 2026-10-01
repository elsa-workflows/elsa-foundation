using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// Runs <see cref="WorkflowActivationCrashRepairContract"/> on PostgreSQL, where completing an interrupted activation
/// can race another node that moves the slot on (#2193), and where two completions of one slot can interleave inside a
/// projection switch (#2265). Each scenario gets its own persistence scope.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeWorkflowActivationCrashRepairPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    public static TheoryData<string> Scenarios => WorkflowActivationCrashRepairContract.Scenarios;

    public static TheoryData<string> ConcurrentCompletionScenarios => WorkflowActivationCrashRepairContract.ConcurrentCompletionScenarios;

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
    [MemberData(nameof(ConcurrentCompletionScenarios))]
    public Task PostgreSql_concurrent_completions(string scenario) => RunConcurrentAsync(scenario);

    [SkippableTheory]
    [MemberData(nameof(ConcurrentSwitchScenarios))]
    public Task PostgreSql_concurrent_switches(string scenario) => RunConcurrentAsync(scenario);

    private async Task RunConcurrentAsync(string scenario)
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
