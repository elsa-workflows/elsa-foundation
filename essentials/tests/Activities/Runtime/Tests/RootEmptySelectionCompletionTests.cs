using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 1208 FR-024 / Q3: the workflow's root treats an explicitly empty selection like any other successful completion.
/// The selection only routes inside a routing parent; a parentless root that completes ends the workflow.
/// </summary>
public sealed class RootEmptySelectionCompletionTests
{
    [Fact]
    public async Task A_root_that_completes_with_an_empty_selection_completes_the_workflow()
    {
        await using var harness = WorkflowExecutionHarness.Create().Build("actexec-00", "actexec-01", "actexec-02");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(new ExecutableNode(
            executableNodeId: "node-root",
            authoredActivityId: "authored-node-root",
            activityType: typeof(SelectsNothing).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>())));

        run.AssertCompleted("node-root");
        run.AssertOutcomes("node-root");
        run.AssertWorkflowCompleted();
    }

    /// <summary>Declares A and Done and completes with an explicitly empty selection.</summary>
    [ActivityOutcome("A")]
    [ActivityOutcome("Done")]
    public sealed class SelectsNothing : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, OutcomeSelection.Empty));
    }
}
