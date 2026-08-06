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

    [Fact]
    public void Generic_checkpoint_provenance_round_trips_with_semantic_equality()
    {
        var provenance = new RuntimeCheckpointProvenance(
            new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string> { ["opaque-domain-key"] = "opaque-value" }),
            7);
        var checkpoint = new RuntimeCheckpoint(
            "checkpoint-provenance", "Checkpoint", "execution-depth", DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>())
        {
            Provenance = provenance
        };

        var checkpointCopy = RoundTrip(checkpoint);

        Assert.Equal(provenance, checkpointCopy.Provenance);
        Assert.Equal(7, checkpointCopy.Provenance!.WorkflowCheckpointOrder);
        Assert.Equal("opaque-value", checkpointCopy.Provenance.ExecutionContext.Entries["opaque-domain-key"]);
    }

    [Fact]
    public void Legacy_checkpoint_defaults_to_no_provenance_and_preserves_its_json_shape()
    {
        var checkpoint = new RuntimeCheckpoint(
            "checkpoint-legacy", "Checkpoint", "execution-depth", DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>());
        var legacyShape = new
        {
            checkpoint.CheckpointId,
            checkpoint.Name,
            checkpoint.WorkflowExecutionId,
            checkpoint.OccurredAt,
            checkpoint.ActivityExecutionIds,
            checkpoint.Metadata
        };
        var checkpointJson = JsonSerializer.SerializeToNode(checkpoint)!.AsObject();

        Assert.False(checkpointJson.ContainsKey("Provenance"));
        Assert.Equal(JsonSerializer.Serialize(legacyShape), JsonSerializer.Serialize(checkpoint));
        Assert.Null(checkpointJson.Deserialize<RuntimeCheckpoint>()!.Provenance);

        var stateJson = JsonSerializer.SerializeToNode(NewState())!.AsObject();
        Assert.False(stateJson.ContainsKey("CheckpointOrderHighWatermarks"));
    }

    [Fact]
    public void Checkpoint_receipt_has_structural_value_equality_across_durable_collection_shapes()
    {
        var receipt = new RuntimeCheckpointCommitStoreResult(["outbox-1", "outbox-2"])
        {
            Status = RuntimeCheckpointCommitStoreStatus.Committed,
            CommitFingerprint = "fingerprint",
            ConsumedSchedulerWorkItemIds = ["work-1"]
        };
        var rehydrated = new RuntimeCheckpointCommitStoreResult(new List<string> { "outbox-1", "outbox-2" })
        {
            Status = RuntimeCheckpointCommitStoreStatus.Committed,
            CommitFingerprint = "fingerprint",
            ConsumedSchedulerWorkItemIds = new List<string> { "work-1" }
        };

        Assert.Equal(receipt, rehydrated);
        Assert.Equal(receipt.GetHashCode(), rehydrated.GetHashCode());
        Assert.NotEqual(receipt, rehydrated with { FailureCode = "changed" });
    }

    [Fact]
    public void Generic_runtime_checkpoint_contract_contains_no_execution_evidence_identifier()
    {
        var publicContractNames = typeof(RuntimeCheckpoint).Assembly.GetExportedTypes()
            .Select(type => type.FullName ?? type.Name);

        Assert.DoesNotContain(publicContractNames, name => name.Contains("ExecutionEvidence", StringComparison.Ordinal));
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
