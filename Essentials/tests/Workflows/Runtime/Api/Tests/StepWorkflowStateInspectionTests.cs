using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class StepWorkflowStateInspectionTests
{
    [Fact]
    public void Current_name_and_correlation_use_the_same_existing_identity_permission()
    {
        var state = new WorkflowExecutionState("run", new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Completed, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, "correlation-sentinel", null, null,
            new Dictionary<string, string> { [RuntimeMetadataKeys.InstanceName] = "name-sentinel" });
        var visible = WorkflowInstanceSummaryView.From(state, canInspectSensitiveValues: true);
        Assert.Equal("correlation-sentinel", visible.CorrelationId);
        Assert.Equal("name-sentinel", visible.InstanceName);
        var denied = WorkflowInstanceSummaryView.From(state, canInspectSensitiveValues: false);
        Assert.Null(denied.CorrelationId);
        Assert.Null(denied.InstanceName);
        Assert.DoesNotContain("sentinel", JsonSerializer.Serialize(denied), StringComparison.Ordinal);
    }

    [Fact]
    public void Accepted_effect_kinds_survive_inspection_projection_without_payload_or_state_access()
    {
        var state = Activity(new Dictionary<string, string> { [StepWorkflowStateEffects.MetadataKey] = "[\"outputs\",\"correlationId\",\"outputs\",\"unrecognized\"]" });
        var projection = ActivityExecutionInspectionProjection.FromState(state, "checkpoint", DateTimeOffset.UnixEpoch);
        var detail = ActivityExecutionInspectionView.From(projection, canInspectSensitiveValues: false);
        var summary = ActivityExecutionInspectionSummaryView.From(ActivityExecutionInspectionSummaryProjection.FromProjection(projection));
        Assert.Equal(["outputs", "correlationId"], detail.WorkflowStateEffects);
        Assert.Equal(detail.WorkflowStateEffects, summary.WorkflowStateEffects);
        Assert.Empty(detail.ValueSnapshots);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{\"attempted\":\"sentinel\"}")]
    public void Failed_or_malformed_attribution_does_not_invent_committed_changes(string? metadata)
    {
        var data = new Dictionary<string, string>();
        if (metadata is not null) data[StepWorkflowStateEffects.MetadataKey] = metadata;
        var state = Activity(data) with { Status = ActivityExecutionStatus.Faulted };
        var projection = ActivityExecutionInspectionProjection.FromState(state, "fault-checkpoint", DateTimeOffset.UnixEpoch);
        Assert.Empty(ActivityExecutionInspectionView.From(projection).WorkflowStateEffects);
    }

    private static ActivityExecutionState Activity(IReadOnlyDictionary<string, string> metadata) => new(
        new ActivityExecution("body", "run", "node", "node", "Test", "1"), ActivityExecutionStatus.Completed, null,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, null, null, null, [], [], 0, 0, metadata);
}
