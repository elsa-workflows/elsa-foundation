using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed partial class WorkflowControlProducerTests
{
    [Theory]
    [InlineData("missing-notifications")]
    [InlineData("null-notifications")]
    [InlineData("missing-failure")]
    [InlineData("missing-payload")]
    public async Task A_real_committed_root_control_cannot_lose_content_before_its_workflow_terminal_source(string mutation)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(authoredFaultChild: true,
            configureServices: services =>
            {
                foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerDrainObserver) &&
                             descriptor.ImplementationType is { } type && (type == typeof(BlockingIncidentWorkflowFaultObserver) ||
                                 type == typeof(IncidentStrategyResolutionDrainObserver))).ToArray())
                    services.Remove(descriptor);
            });
        var run = await fixture.StartParentAsync("incomplete-control", "incomplete-control-parent", "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        await fixture.SweepAsync();
        var childId = run.Dispatch.ChildWorkflowExecutionId;
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.FindWorkflowAsync(childId))!.Status);
        var root = Assert.Single(await fixture.ListActivitiesAsync(childId));
        Assert.Equal(ActivityExecutionStatus.Faulted, root.Status);
        Assert.Equal("authored-error", root.ProducedControl!.AuthoredFailureCode);
        Assert.Equal(["before-error", "alongside-error"], root.ProducedControl.Notifications.Select(item => item.Code));
        Assert.Contains(fixture.Checkpoints.Commits, commit => commit.WorkflowExecutionId == childId &&
            commit.StateChanges.ActivityExecutions.Any(change => change.State.ProducedControl?.AuthoredFailureCode == "authored-error"));
        Assert.DoesNotContain(fixture.Checkpoints.Commits, commit => commit.WorkflowExecutionId == childId &&
            commit.StateChanges.WorkflowExecution?.State.Status is { } status && status.IsTerminal());
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var original = JsonSerializer.Serialize(root, options);
        Assert.Equal(original, JsonSerializer.Serialize(JsonSerializer.Deserialize<ActivityExecutionState>(original, options), options));
        var node = JsonNode.Parse(original)!.AsObject();
        var batch = node["producedControl"]!.AsObject();
        switch (mutation)
        {
            case "missing-notifications": Assert.True(batch.Remove("notifications")); break;
            case "null-notifications": batch["notifications"] = null; break;
            case "missing-failure": Assert.True(batch.Remove("authoredFailureCode")); break;
            case "missing-payload": Assert.True(batch["notifications"]![0]!.AsObject().Remove("payload")); break;
        }

        Assert.Throws<JsonException>(() => node.Deserialize<ActivityExecutionState>(options));
        Assert.Equal(original, JsonSerializer.Serialize(Assert.Single(await fixture.ListActivitiesAsync(childId)), options));
    }
}
