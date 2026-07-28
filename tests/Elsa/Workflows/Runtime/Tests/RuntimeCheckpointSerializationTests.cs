using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointSerializationTests
{
    private static readonly WorkflowExecutableIdentity Executable =
        new("artifact-depth", "definition-depth", "version-depth", "1.0.0", "sha256:depth");

    [Fact]
    public void Dispatch_depth_round_trips_through_start_command_checkpoint_and_state()
    {
        var authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("caller");
        var request = new WorkflowExecutionStartDispatchRequest(
            Executable.ArtifactId, "caller", null, null, null, null, null, null, null,
            WorkflowRunKind.PublishedRun, null, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy,
            null, null, null, null, authority, null, dispatchNestingDepth: 6);
        var command = new WorkflowExecutionStartCommandPayload(
            Executable, Executable.ArtifactId, null, null, null, null, WorkflowRunKind.PublishedRun,
            null, null, null, null, null, authority, null, dispatchNestingDepth: 6);
        var checkpoint = new RuntimeCheckpointCommandPayload(
            Executable, "WorkflowStarted", [], RuntimeCheckpointCommandPayload.WorkflowStartReason,
            null, null, null, null, null, WorkflowRunKind.PublishedRun, null,
            null, null, null, null, authority, dispatchNestingDepth: 6);
        var state = NewState() with { DispatchNestingDepth = 6 };

        Assert.Equal(6, RoundTrip(request).DispatchNestingDepth);
        Assert.Equal(6, RoundTrip(command).DispatchNestingDepth);
        Assert.Equal(6, RoundTrip(checkpoint).DispatchNestingDepth);
        Assert.Equal(6, RoundTrip(state).DispatchNestingDepth);
    }

    [Fact]
    public void Missing_legacy_dispatch_depth_defaults_to_root_zero()
    {
        var request = new WorkflowExecutionStartDispatchRequest(Executable.ArtifactId, "caller");
        var command = new WorkflowExecutionStartCommandPayload(Executable, Executable.ArtifactId);
        var checkpoint = new RuntimeCheckpointCommandPayload(
            Executable, "WorkflowStarted", [], RuntimeCheckpointCommandPayload.WorkflowStartReason);
        var state = NewState();

        Assert.Equal(0, LegacyRoundTrip(request).DispatchNestingDepth);
        Assert.Equal(0, LegacyRoundTrip(command).DispatchNestingDepth);
        Assert.Equal(0, LegacyRoundTrip(checkpoint).DispatchNestingDepth);
        Assert.Equal(0, LegacyRoundTrip(state).DispatchNestingDepth);
    }

    private static WorkflowExecutionState NewState() =>
        new(
            "execution-depth", Executable, WorkflowExecutionStatus.Running, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null,
            null, null, null, new Dictionary<string, string>());

    private static T RoundTrip<T>(T value) where T : notnull =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private static T LegacyRoundTrip<T>(T value) where T : notnull
    {
        var json = JsonSerializer.SerializeToNode(value)!.AsObject();
        json.Remove("DispatchNestingDepth");
        return json.Deserialize<T>()!;
    }
}
