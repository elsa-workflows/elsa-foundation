using System.Net;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
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

        var callerId = await host.ExecuteAsync(caller);
        var callerRun = await host.WaitForSettledAsync(callerId);
        var child = await host.WaitForActorAsync("dispatch-target");
        Assert.Equal(target.ArtifactId, child.Run.Workflow);
        var targetRun = await host.WaitForSettledAsync(child.View.Header.Ref.Id);

        Assert.True(callerRun.Workflow?.Status == WorkflowExecutionStatus.Completed, callerRun.Describe());
        Assert.True(targetRun.Workflow?.Status == WorkflowExecutionStatus.Completed, targetRun.Describe());
        Assert.Contains(callerRun.Activities, state => state.AuthoredActivityId == "after" && state.Status == ActivityExecutionStatus.Completed);
        if (callerFixture == "execute-caller")
        {
            Assert.Equal(callerId, child.View.Header.Parent?.Id);
            Assert.True(targetRun.Workflow!.CompletedAt <= callerRun.Workflow!.CompletedAt,
                $"target {targetRun.Workflow.CompletedAt:O}, caller {callerRun.Workflow.CompletedAt:O}");
        }
    }
}
