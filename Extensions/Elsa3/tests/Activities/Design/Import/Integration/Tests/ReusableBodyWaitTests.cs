using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// A reusable body that waits (spec 246 guidance review, G6): Elsa 3 suspends inside a workflow-as-activity and resumes
/// there. This pins what an imported reusable activity does when its graph body suspends on a Delay.
/// </summary>
public sealed class ReusableBodyWaitTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_reusable_body_that_waits_resumes_and_completes()
    {
        await using var host = await Elsa3ImportHost.StartAsync(withScheduling: true);
        var (apply, items) = await host.ImportAsync("import-waits", "reusable-waits");
        Assert.True((int)apply.StatusCode < 300, apply.Body.ToJsonString());
        var reusable = items.Single(item => (bool?)item["isReusable"] == true);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));

        var run = await host.RunAsync(wrapper, new JsonObject());
        foreach (var state in run.Activities)
            output.WriteLine($"{state.Execution.ActivityType} {state.Status} {state.StartedAt:O} -> {state.CompletedAt:O}");

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var activity = Assert.Single(run.Activities, state => state.Execution.ActivityType == Elsa3ImportHost.Required(reusable, "activityTypeKey"));
        Assert.Equal("Approved", activity.Completion?.OutcomeKey);
        // The Delay suspended and resumed: it completed, and no earlier than its two seconds after it started.
        var delay = Assert.Single(run.Activities, state => state.Execution.ActivityType.EndsWith(".Delay", StringComparison.Ordinal));
        Assert.Equal(ActivityExecutionStatus.Completed, delay.Status);
        Assert.True(delay.CompletedAt - delay.StartedAt >= TimeSpan.FromSeconds(2), $"Delay ran from {delay.StartedAt:O} to {delay.CompletedAt:O}.");
    }
}
