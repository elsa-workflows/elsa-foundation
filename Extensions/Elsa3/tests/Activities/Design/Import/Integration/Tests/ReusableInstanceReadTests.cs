using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Elsa 3's <c>getWorkflowInstanceId()</c> and <c>getCorrelationId()</c> convert to a workflow-instance parameter
/// (spec 247), which reads the running instance rather than a value the graph would have to hold, so a reusable body may
/// use them. The readiness guide (docs/reference/elsa3-migration-readiness.md) relies on this proof.
/// </summary>
public sealed class ReusableInstanceReadTests
{
    [Fact]
    public async Task A_reusable_body_reads_the_running_instance_id_and_correlation_id()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var (apply, items) = await host.ImportAsync("import-instance-reader", "reusable-reads-instance");
        Assert.True((int)apply.StatusCode < 300, apply.Body.ToJsonString());
        var reusable = items.Single(item => (bool?)item["isReusable"] == true);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));

        var run = await host.RunAsync(wrapper, new JsonObject());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var activity = Assert.Single(run.Activities, state => state.Execution.ActivityType == Elsa3ImportHost.Required(reusable, "activityTypeKey"));
        Assert.Equal("Approved", activity.Completion?.OutcomeKey);
    }
}
