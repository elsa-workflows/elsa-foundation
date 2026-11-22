using System.Diagnostics;
using System.Net;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// The guidance for a refused reusable says to import it as a regular workflow and call it with Dispatch Workflow. This
/// proves that alternative: an imported Elsa 3 workflow that dispatches (or executes) another imported workflow by its
/// Elsa 3 definition id starts it, because the import keeps the Elsa 3 definition id as the Elsa 4 one.
/// </summary>
public sealed class DispatchWorkflowCallTests
{
    [Theory]
    [InlineData("dispatch-caller")]
    [InlineData("execute-caller")]
    public async Task An_imported_workflow_starts_another_imported_workflow_by_its_Elsa_3_definition_id(string callerFixture)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync($"import-{callerFixture}", ["dispatch-target", callerFixture]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        Assert.Equal("dispatch-target", Elsa3ImportHost.Required(import.Item("dispatch-target-v1"), "workflowDefinitionId"));

        // The target is published first: publishing the caller pins the target's live published artifact.
        var target = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("dispatch-target-v1"), "workflowVersionId"));
        var caller = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item($"{callerFixture}-v1"), "workflowVersionId"));

        var callerRun = await host.RunAsync(caller);
        var targetRun = await host.WaitForSettledAsync(await TargetExecutionIdAsync(host, target));

        Assert.True(callerRun.Workflow?.Status == WorkflowExecutionStatus.Completed, callerRun.Describe());
        Assert.True(targetRun.Workflow?.Status == WorkflowExecutionStatus.Completed, targetRun.Describe());
        Assert.Contains(callerRun.Activities, state => state.Execution.ExecutableNodeId == "after" && state.Status == ActivityExecutionStatus.Completed);
        if (callerFixture == "execute-caller")
        {
            // Execute Workflow waits for the target, so the caller completes after it.
            Assert.True(targetRun.Workflow!.CompletedAt <= callerRun.Workflow!.CompletedAt,
                $"target {targetRun.Workflow.CompletedAt:O}, caller {callerRun.Workflow.CompletedAt:O}");
        }
    }

    /// <summary>The execution started from <paramref name="target"/>'s artifact; the dispatch may start it after the caller's request.</summary>
    private static async Task<string> TargetExecutionIdAsync(Elsa3ImportHost host, PublishedWorkflow target)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var executions = await host.ReadAsync(services => services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync().AsTask());
            if (executions.SingleOrDefault(execution => execution.PinnedExecutable.ArtifactId == target.ArtifactId) is { } execution)
                return execution.WorkflowExecutionId;
            Assert.True(deadline.Elapsed < Elsa3ImportHost.SettleTimeout, "No execution of the dispatched target was started.");
            await Task.Delay(100);
        }
    }
}
