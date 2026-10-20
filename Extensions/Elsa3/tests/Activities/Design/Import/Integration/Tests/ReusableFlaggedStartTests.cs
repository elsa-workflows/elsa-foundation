using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Spec 246 D-6 refuses a reusable workflow that starts from a trigger, but 'Can start workflow' ticked on a non-trigger
/// only picks the flowchart's start node (spec 248). Such a reusable imports, publishes and starts its graph body where
/// Elsa 3 did: at the flagged WriteLine, not at the WriteLine that comes first. The readiness guide
/// (docs/reference/elsa3-migration-readiness.md) relies on this proof.
/// </summary>
public sealed class ReusableFlaggedStartTests
{
    [Fact]
    public async Task A_reusable_with_a_flagged_non_trigger_imports_and_starts_there()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var (apply, items) = await host.ImportAsync("import-flagged-start", "reusable-flagged-start");
        Assert.True((int)apply.StatusCode < 300, apply.Body.ToJsonString());
        var reusable = items.Single(item => (bool?)item["isReusable"] == true);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));

        var run = await host.RunAsync(wrapper, new JsonObject());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var activity = Assert.Single(run.Activities, state => state.Execution.ActivityType == Elsa3ImportHost.Required(reusable, "activityTypeKey"));
        Assert.Equal("Approved", activity.Completion?.OutcomeKey);
    }
}
