// EF command budgets: how many EF commands each reference workflow may cost, per persistence mode, on
// SQLite. The count is deterministic and is a correctness gate, not a performance measurement; nothing here is timed.
//
// The budgets live in ef-command-budgets.json next to this file. Lowering a budget is a normal change. Raising one
// needs a stated reason in the pull request that raises it.
using System.Text.Json;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Runs each reference workflow on a fresh SQLite database under each persistence mode and fails when the
/// EF commands it costs exceed the committed budget. Only the run itself is counted: host start, schema installation
/// and publication come before the count, and the assertions that check the outcome come after it.
/// </summary>
public sealed class EfCommandBudgetTests(ITestOutputHelper output)
{
    private const string BudgetFile = "ef-command-budgets.json";

    public static TheoryData<string, PersistenceMode> Cases()
    {
        var cases = new TheoryData<string, PersistenceMode>();
        foreach (var workflow in ReferenceWorkflowRuns.Workflows)
        foreach (var mode in Enum.GetValues<PersistenceMode>())
            cases.Add(workflow, mode);
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Reference_workflow_stays_within_its_EF_command_budget(string workflow, PersistenceMode mode)
    {
        var actual = await MeasureAsync(workflow, mode);

        int? budget = ReadBudgets().TryGetValue(workflow, out var budgets) && budgets.TryGetValue(mode.ToString(), out var committed)
            ? committed
            : null;
        output.WriteLine($"EF commands: workflow '{workflow}', mode '{mode}': actual {actual}, budget {budget?.ToString() ?? "none"}.");
        Assert.True(budget is not null,
            $"No EF command budget for workflow '{workflow}' under mode '{mode}' (actual {actual}). Add it to {BudgetFile}.");
        Assert.True(actual <= budget,
            $"EF command budget exceeded for workflow '{workflow}' under mode '{mode}': actual {actual}, budget {budget}. " +
            $"Raising a budget in {BudgetFile} needs a stated reason in the pull request.");
    }

    [Fact]
    public void Every_budget_names_a_reference_workflow_and_a_persistence_mode()
    {
        var modes = Enum.GetNames<PersistenceMode>();
        foreach (var (workflow, budgets) in ReadBudgets())
        {
            Assert.True(ReferenceWorkflowRuns.Workflows.Contains(workflow), $"{BudgetFile} budgets unknown workflow '{workflow}'.");
            foreach (var mode in budgets.Keys)
                Assert.True(modes.Contains(mode), $"{BudgetFile} budgets workflow '{workflow}' under unknown mode '{mode}'.");
        }
    }

    private static async Task<int> MeasureAsync(string workflow, PersistenceMode mode)
    {
        var capture = new CommandCaptureInterceptor();
        await using var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(mode, ReferenceWorkflowRuns.MaxActivitiesPerCommit, services =>
        {
            ReferenceWorkflowRuns.ComposeHost(services);
            services.ConfigureDbContext<RuntimeSqliteDbContext>(builder => builder.AddInterceptors(capture));
        });

        var commands = 0;
        await ReferenceWorkflowRuns.RunAsync(workflow, fixture, async run =>
        {
            var before = capture.Commands.Count;
            await run();
            commands += capture.Commands.Count - before;
        });
        return commands;
    }

    private static Dictionary<string, Dictionary<string, int>> ReadBudgets() =>
        JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(
            File.ReadAllText(Path.Join(AppContext.BaseDirectory, "CommandBudgets", BudgetFile)))
        ?? throw new InvalidDataException($"{BudgetFile} is empty.");
}
