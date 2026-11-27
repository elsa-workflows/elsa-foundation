using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Models;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// #5850: an Elsa 3 reusable whose workflow variables have no value (an Object and a String) imports, publishes and runs.
/// The graph manifest leaves their <c>initialValue</c> out, so each starts at its type's default.
/// </summary>
public sealed class ReusableValuelessVariablesTests
{
    [Fact]
    public async Task A_reusable_whose_variables_have_no_value_publishes_and_runs()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var (apply, items) = await host.ImportAsync("import-valueless-variables", "reusable-valueless-variables");
        Assert.True((int)apply.StatusCode < 300, apply.Body.ToJsonString());
        var state = Assert.Single(apply.Body["publications"]!.AsArray());
        Assert.True((int?)state!["status"] == (int)ReusableActivityPublicationStatus.Published, state.ToJsonString());
        var reusable = items.Single(item => (bool?)item["isReusable"] == true);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));

        var run = await host.RunAsync(wrapper, new JsonObject());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var activity = Assert.Single(run.Activities, activityState => activityState.Execution.ActivityType == Elsa3ImportHost.Required(reusable, "activityTypeKey"));
        Assert.Equal(new[] { "Approved" }, activity.Completion?.OutcomeNames);
    }
}
