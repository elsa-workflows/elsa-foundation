using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model.State;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnStateCodecTests
{
    public static TheoryData<int, string, bool> IncompletePersistedResponsibility()
    {
        var cases = new TheoryData<int, string, bool>();
        foreach (var version in new[] { 1, 2 })
        {
            foreach (var path in new[] { "races", "loops", "compensables", "compensationRuns", "cancelling",
                         "compensationRuns/0/pendingCompensableIds", "compensables/0/status" })
            {
                cases.Add(version, path, false);
                cases.Add(version, path, true);
            }
            foreach (var path in new[] { "pendingFault", "tokens/1/parentTokenId", "tokens/1/kind",
                         "tokens/1/flowId", "tokens/1/iterationKey", "activeChildren/0/iterationId" })
                cases.Add(version, path.Replace("activeChildren", version == 1 ? "activeChildren" : "activeWork"), false);
            cases.Add(version, "tokens/1/" + (version == 1 ? "producingActivityExecutionId" : "producingWorkHandle"), false);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(IncompletePersistedResponsibility))]
    public void Actual_compensation_checkpoint_cannot_default_missing_responsibility(int version, string path, bool explicitNull)
    {
        var root = CompensationPayload(version);
        var parts = path.Split('/');
        JsonNode current = root;
        foreach (var part in parts[..^1])
            current = current is JsonArray array ? array[int.Parse(part)]! : current[part]!;
        var owner = current.AsObject();
        Assert.True(owner.ContainsKey(parts[^1]));
        if (explicitNull) owner[parts[^1]] = null;
        else Assert.True(owner.Remove(parts[^1]));
        var checkpoint = Document(root.ToJsonString(), version);
        var original = checkpoint.Value.InlineValue!.Value.GetRawText();

        Assert.Throws<BpmnHostException>(() => new BpmnStateCodec().Read(checkpoint));

        Assert.Equal(original, checkpoint.Value.InlineValue!.Value.GetRawText());
    }

    [Theory]
    [InlineData(1, "reversed-head")]
    [InlineData(2, "reversed-head")]
    [InlineData(1, "missing-handler-parent")]
    [InlineData(2, "missing-handler-parent")]
    [InlineData(1, "unknown-pending-id")]
    [InlineData(2, "unknown-pending-id")]
    [InlineData(1, "duplicate-registration-id")]
    [InlineData(2, "duplicate-registration-id")]
    [InlineData(1, "undefined-registration-status")]
    [InlineData(2, "undefined-registration-status")]
    [InlineData(1, "null-registration")]
    [InlineData(2, "null-registration")]
    public void Actual_compensation_checkpoint_refuses_inconsistent_replay_identity(int version, string defect)
    {
        var root = CompensationPayload(version);
        var pending = root["compensationRuns"]![0]!["pendingCompensableIds"]!.AsArray();
        switch (defect)
        {
            case "reversed-head":
                var first = pending[0]!.DeepClone();
                pending[0] = pending[1]!.DeepClone();
                pending[1] = first;
                break;
            case "missing-handler-parent": root["tokens"]![1]!["parentTokenId"] = null; break;
            case "unknown-pending-id": pending[0] = "unknown-registration"; break;
            case "duplicate-registration-id": root["compensables"]![1]!["compensableId"] = root["compensables"]![0]!["compensableId"]!.DeepClone(); break;
            case "undefined-registration-status": root["compensables"]![0]!["status"] = 99; break;
            case "null-registration": root["compensables"]![0] = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        var checkpoint = Document(root.ToJsonString(), version);
        var original = checkpoint.Value.InlineValue!.Value.GetRawText();

        Assert.Throws<BpmnHostException>(() => new BpmnStateCodec().Read(checkpoint));

        Assert.Equal(original, checkpoint.Value.InlineValue!.Value.GetRawText());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Actual_compensation_checkpoint_refuses_case_shadowed_responsibility(int version, bool nested)
    {
        var root = CompensationPayload(version);
        if (nested) root["compensationRuns"]![0]!["PendingCompensableIds"] = null;
        else root["CompensationRuns"] = null;
        var checkpoint = Document(root.ToJsonString(), version);
        var original = checkpoint.Value.InlineValue!.Value.GetRawText();

        Assert.Throws<BpmnHostException>(() => new BpmnStateCodec().Read(checkpoint));

        Assert.Equal(original, checkpoint.Value.InlineValue!.Value.GetRawText());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Complete_actual_compensation_shape_keeps_original_replay_order(int version)
    {
        var original = CompensationPayload(version);
        var read = new BpmnStateCodec().Read(Document(original.ToJsonString(), version));
        Assert.Equal(version, read.Version);
        Assert.Equal(new[] { "comp:19", "comp:10" }, Assert.Single(read.State.CompensationRuns).PendingCompensableIds);
        Assert.Equal("token:22", read.State.Tokens.Single(token => token.AtElementId == "handler-b").ParentTokenId);
        Assert.Equal(2, read.State.Compensables.Count);
    }

    [Fact]
    public void Scope_correlation_keys_retain_distinct_case_sensitive_identities()
    {
        var codec = new BpmnStateCodec();
        var fresh = codec.Read(null, new Dictionary<string, string> { ["Event"] = "first", ["event"] = "second" });
        var payload = codec.Write(fresh, fresh.State).InlineValue!.Value;

        var read = codec.Read(Document(payload.GetRawText(), 2));

        Assert.Equal(2, read.InvocationCorrelation.Count);
        Assert.Equal("first", read.InvocationCorrelation["Event"]);
        Assert.Equal("second", read.InvocationCorrelation["event"]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Deferred_fault_text_is_preserved_without_a_new_nonblank_policy(int version)
    {
        var root = CompensationPayload(version);
        root["pendingFault"] = new JsonObject { ["faultCode"] = "original.code", ["message"] = "" };
        var codec = new BpmnStateCodec();

        var read = codec.Read(Document(root.ToJsonString(), version));
        var written = codec.Write(read, read.State).InlineValue!.Value;

        Assert.Equal("original.code", read.State.PendingFault!.FaultCode);
        Assert.Equal("", read.State.PendingFault.Message);
        Assert.Equal("", written.GetProperty("pendingFault").GetProperty("message").GetString());
    }

    [Fact]
    public void Explicit_json_null_is_a_valid_snapshotted_collection_item()
    {
        var codec = new BpmnStateCodec();
        var state = new BpmnExecutionState(tokens: [new BpmnToken("loop-token", "collection")],
            loops: [new BpmnLoopState("loop", "loop-token", "collection", true, 1, 0, 0,
                [JsonSerializer.SerializeToElement<object?>(null)])]);
        var payload = codec.Write(codec.Read(null), state).InlineValue!.Value;

        var read = codec.Read(Document(payload.GetRawText(), 2));

        Assert.Equal(JsonValueKind.Null, Assert.Single(Assert.Single(read.State.Loops).Items!).ValueKind);
    }

    private static JsonObject CompensationPayload(int version)
    {
        using var resource = typeof(BpmnStateCodecTests).Assembly.GetManifestResourceStream("Bpmn.LegacyCompensationState.json")!;
        var payload = JsonNode.Parse(resource)!["payload"]!.DeepClone().AsObject();
        if (version == 1) return payload;
        var codec = new BpmnStateCodec();
        var original = codec.Read(Document(payload.ToJsonString()));
        return JsonNode.Parse(codec.Write(original with { Version = 2 }, original.State).InlineValue!.Value.GetRawText())!.AsObject();
    }
}
