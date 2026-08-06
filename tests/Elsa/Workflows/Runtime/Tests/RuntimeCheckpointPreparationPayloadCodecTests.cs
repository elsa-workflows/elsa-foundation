using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointPreparationPayloadCodecTests
{
    private static readonly WorkflowExecutableIdentity Executable =
        new("artifact-codec", "definition-codec", "version-codec", "1.0.0", "sha256:codec");

    [Fact]
    public void Encode_canonicalizes_raw_request_and_decode_returns_a_detached_graph()
    {
        using var businessPayloadDocument = JsonDocument.Parse("""
        {
          "rawCommit": {
            "checkpoint": {
              "expectedFence": { "provenance": "business-nested" },
              "provenance": "business-checkpoint"
            },
            "expectedFence": "business-fence"
          },
          "checkpoint": "business-root-checkpoint",
          "expectedFence": "business-root-fence",
          "provenance": "business-root-provenance"
        }
        """);
        var checkpointActivityIds = new List<string> { "activity-1" };
        var checkpointMetadata = new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" };
        var stateMetadata = new Dictionary<string, string> { ["state"] = "before" };
        var commitMetadata = new Dictionary<string, string> { ["commit"] = "before" };
        var intents = new List<RuntimePostCommitIntent> { NewIntent(businessPayloadDocument.RootElement) };
        var request = new RuntimeCheckpointPrepareRequest(
            NewCommit(checkpointActivityIds, checkpointMetadata, stateMetadata, intents, commitMetadata),
            "source-codec",
            "operation-codec",
            new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string> { ["session"] = "before" }));
        var codec = new RuntimeCheckpointPreparationPayloadCodec();

        var envelope = codec.Encode(request);

        checkpointActivityIds.Add("activity-after-encode");
        checkpointMetadata["a"] = "after";
        stateMetadata["state"] = "after";
        commitMetadata["commit"] = "after";
        intents.Clear();
        var decoded = codec.Decode(envelope);

        Assert.Equal(RuntimeCheckpointPreparationPayloadCodec.CurrentVersion, envelope.Version);
        Assert.Equal(RuntimeCheckpointPreparationPayloadCodec.PayloadSha256Length, envelope.PayloadSha256.Length);
        Assert.Equal("sha256", RuntimeCheckpointPreparationPayloadCodec.PayloadHashAlgorithm);
        Assert.Equal("first", decoded.Commit.Checkpoint.Metadata["a"]);
        Assert.Equal(["activity-1"], decoded.Commit.Checkpoint.ActivityExecutionIds);
        Assert.Equal("before", decoded.Commit.StateChanges.WorkflowExecution!.Metadata["state"]);
        Assert.Equal("before", decoded.Commit.Metadata["commit"]);
        Assert.Equal("before", decoded.RequestedExecutionContext.Entries["session"]);
        Assert.Single(decoded.Commit.PostCommitIntents);
        var decodedBusinessPayload = Assert.Single(decoded.Commit.PostCommitIntents).Payload!.Value;
        Assert.True(JsonElement.DeepEquals(businessPayloadDocument.RootElement, decodedBusinessPayload));
        Assert.Null(decoded.Commit.Checkpoint.Provenance);
        Assert.Null(decoded.Commit.ExpectedFence);
        Assert.NotSame(request.Commit.Checkpoint.ActivityExecutionIds, decoded.Commit.Checkpoint.ActivityExecutionIds);
        Assert.NotSame(request.Commit.Metadata, decoded.Commit.Metadata);

        var canonicalJson = JsonNode.Parse(envelope.CanonicalPayload)!.AsObject();
        var rawCommit = canonicalJson["rawCommit"]!.AsObject();
        Assert.False(rawCommit.ContainsKey("expectedFence"));
        Assert.False(rawCommit["checkpoint"]!.AsObject().ContainsKey("provenance"));

        Assert.Equal(envelope, codec.Encode(decoded));
    }

    [Fact]
    public void Decode_rejects_unknown_version_invalid_json_digest_mismatch_and_noncanonical_payload()
    {
        var codec = new RuntimeCheckpointPreparationPayloadCodec();
        var envelope = codec.Encode(new RuntimeCheckpointPrepareRequest(
            NewCommit([], new Dictionary<string, string>(), new Dictionary<string, string>(), [], new Dictionary<string, string>()),
            "source-codec",
            "operation-codec",
            RuntimeExecutionContextSnapshot.Empty));

        Assert.Throws<ArgumentOutOfRangeException>(() => codec.Decode(envelope with { Version = 2 }));
        Assert.Throws<ArgumentNullException>(() => codec.Decode(null!));
        Assert.Throws<ArgumentNullException>(() => codec.Decode(envelope with { CanonicalPayload = null! }));
        Assert.Throws<ArgumentException>(() => codec.Decode(envelope with { CanonicalPayload = " " }));
        Assert.Throws<ArgumentNullException>(() => codec.Decode(envelope with { PayloadSha256 = null! }));
        Assert.Throws<ArgumentException>(() => codec.Decode(envelope with { PayloadSha256 = " " }));
        Assert.Throws<ArgumentException>(() => codec.Decode(envelope with { PayloadSha256 = "abc" }));
        Assert.Throws<ArgumentException>(() => codec.Decode(envelope with { PayloadSha256 = new string('A', RuntimeCheckpointPreparationPayloadCodec.PayloadSha256Length) }));
        Assert.Throws<ArgumentException>(() => codec.Decode(envelope with { PayloadSha256 = new string('g', RuntimeCheckpointPreparationPayloadCodec.PayloadSha256Length) }));
        Assert.Throws<ArgumentException>(() => codec.Decode(envelope with { PayloadSha256 = new string('0', RuntimeCheckpointPreparationPayloadCodec.PayloadSha256Length) }));
        Assert.Throws<ArgumentException>(() => codec.Decode(new RuntimeCheckpointPreparationPayloadEnvelope(
            RuntimeCheckpointPreparationPayloadCodec.CurrentVersion,
            "not-json",
            Sha256("not-json"))));
        Assert.Throws<ArgumentException>(() => codec.Decode(new RuntimeCheckpointPreparationPayloadEnvelope(
            RuntimeCheckpointPreparationPayloadCodec.CurrentVersion,
            " " + envelope.CanonicalPayload,
            Sha256(" " + envelope.CanonicalPayload))));

        var domainInvalidPayload = JsonNode.Parse(envelope.CanonicalPayload)!.AsObject();
        domainInvalidPayload["requestedExecutionContext"]!["version"] = 2;
        var domainInvalidJson = domainInvalidPayload.ToJsonString();
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.Decode(new RuntimeCheckpointPreparationPayloadEnvelope(
            RuntimeCheckpointPreparationPayloadCodec.CurrentVersion,
            domainInvalidJson,
            Sha256(domainInvalidJson))));

        var schemaMismatch = JsonNode.Parse(envelope.CanonicalPayload)!.AsObject();
        schemaMismatch["schemaVersion"] = 2;
        Assert.Throws<ArgumentException>(() => codec.Decode(Envelope(schemaMismatch)));

        var missingRawCommit = JsonNode.Parse(envelope.CanonicalPayload)!.AsObject();
        missingRawCommit.Remove("rawCommit");
        Assert.Throws<ArgumentException>(() => codec.Decode(Envelope(missingRawCommit)));

        var missingSourceIdentity = JsonNode.Parse(envelope.CanonicalPayload)!.AsObject();
        missingSourceIdentity.Remove("sourceIdentity");
        Assert.Throws<ArgumentNullException>(() => codec.Decode(Envelope(missingSourceIdentity)));

        var blankOperationIdentity = JsonNode.Parse(envelope.CanonicalPayload)!.AsObject();
        blankOperationIdentity["operationIdentity"] = " ";
        Assert.Throws<ArgumentException>(() => codec.Decode(Envelope(blankOperationIdentity)));
    }

    [Fact]
    public void Decode_rejects_null_checkpoint_state_changes_and_requested_context()
    {
        var codec = new RuntimeCheckpointPreparationPayloadCodec();
        var envelope = codec.Encode(new RuntimeCheckpointPrepareRequest(
            NewCommit([], new Dictionary<string, string>(), new Dictionary<string, string>(), [], new Dictionary<string, string>()),
            "source-codec",
            "operation-codec",
            RuntimeExecutionContextSnapshot.Empty));

        var nullCheckpoint = JsonNode.Parse(envelope.CanonicalPayload)!.AsObject();
        nullCheckpoint["rawCommit"]!["checkpoint"] = null;
        Assert.Throws<ArgumentNullException>(() => codec.Decode(Envelope(nullCheckpoint)));

        var nullStateChanges = JsonNode.Parse(envelope.CanonicalPayload)!.AsObject();
        nullStateChanges["rawCommit"]!["stateChanges"] = null;
        Assert.Throws<ArgumentNullException>(() => codec.Decode(Envelope(nullStateChanges)));

        var nullRequestedContext = JsonNode.Parse(envelope.CanonicalPayload)!.AsObject();
        nullRequestedContext["requestedExecutionContext"] = null;
        Assert.Throws<ArgumentNullException>(() => codec.Decode(Envelope(nullRequestedContext)));
    }

    [Fact]
    public void Encode_rejects_null_or_invalid_request_identity()
    {
        var codec = new RuntimeCheckpointPreparationPayloadCodec();
        var commit = NewCommit([], new Dictionary<string, string>(), new Dictionary<string, string>(), [], new Dictionary<string, string>());

        Assert.Throws<ArgumentNullException>(() => codec.Encode(null!));
        Assert.Throws<ArgumentNullException>(() => codec.Encode(new RuntimeCheckpointPrepareRequest(null!, "source", "operation", RuntimeExecutionContextSnapshot.Empty)));
        Assert.Throws<ArgumentNullException>(() => codec.Encode(new RuntimeCheckpointPrepareRequest(commit, "source", "operation", null!)));
        Assert.Throws<ArgumentException>(() => codec.Encode(new RuntimeCheckpointPrepareRequest(commit, " ", "operation", RuntimeExecutionContextSnapshot.Empty)));
        Assert.Throws<ArgumentException>(() => codec.Encode(new RuntimeCheckpointPrepareRequest(commit, "source", " ", RuntimeExecutionContextSnapshot.Empty)));
    }

    private static RuntimeCheckpointCommit NewCommit(
        IReadOnlyCollection<string> checkpointActivityIds,
        IReadOnlyDictionary<string, string> checkpointMetadata,
        IReadOnlyDictionary<string, string> stateMetadata,
        IReadOnlyList<RuntimePostCommitIntent> intents,
        IReadOnlyDictionary<string, string> commitMetadata)
    {
        var workflowState = new WorkflowExecutionState(
            "workflow-codec",
            Executable,
            WorkflowExecutionStatus.Running,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>());
        var changes = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>("workflow-codec", RuntimeStateChangeOperation.Upsert, workflowState, stateMetadata),
            null,
            [],
            [],
            [],
            [],
            []);
        var checkpoint = new RuntimeCheckpoint(
            "checkpoint-codec",
            "CodecCheckpoint",
            "workflow-codec",
            DateTimeOffset.UnixEpoch,
            checkpointActivityIds,
            checkpointMetadata)
        {
            Provenance = new RuntimeCheckpointProvenance(RuntimeExecutionContextSnapshot.Empty, 7)
        };
        return new RuntimeCheckpointCommit("commit-codec", checkpoint, changes, intents, commitMetadata)
        {
            ExpectedFence = new RuntimeExecutionFence("lease-codec", "owner-codec", 3)
        };
    }

    private static RuntimePostCommitIntent NewIntent(JsonElement payload)
    {
        return new RuntimePostCommitIntent(
            "intent-codec",
            "workflow-codec",
            "runtime.codec",
            DateTimeOffset.UnixEpoch,
            null,
            null,
            payload,
            new Dictionary<string, string> { ["intent"] = "before" });
    }

    private static RuntimeCheckpointPreparationPayloadEnvelope Envelope(JsonNode payload)
    {
        var json = payload.ToJsonString();
        return new RuntimeCheckpointPreparationPayloadEnvelope(
            RuntimeCheckpointPreparationPayloadCodec.CurrentVersion,
            json,
            Sha256(json));
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
