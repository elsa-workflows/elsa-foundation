using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// Runs <see cref="WorkflowActivationCrashRepairContract"/> on PostgreSQL, where completing an interrupted activation
/// can race another node that moves the slot on (#2193). Each scenario gets its own persistence scope.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeWorkflowActivationCrashRepairPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    public static TheoryData<string> Scenarios => WorkflowActivationCrashRepairContract.Scenarios;

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

    private RuntimePostgreSqlDbContext OpenContext() =>
        new(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(fixture.ConnectionString).Options);
}
