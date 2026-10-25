using System.Text.Json;
using Bpmn.Model.State;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>Translation regression tests; actual upgraded-host proof uses the frozen EF snapshots separately.</summary>
public sealed class BpmnStateCodecTests
{
    private const string LegacyJson = """
        {"tokens":[{"tokenId":"token:7","atElementId":"work","status":1,"producingActivityExecutionId":"child:producer"}],
         "activeChildren":[{"nodeId":"node-work","elementId":"work","tokenId":"token:7","schedulingCause":"flow","iterationId":"iteration:2"}],
         "sequence":17,"terminated":false,"diagnostics":[],"pendingFault":{"faultCode":"original.fault","message":"original message"}}
        """;

    [Fact]
    public void Legacy_translation_preserves_work_producer_and_deferred_fault_that_a_direct_read_loses()
    {
        var direct = JsonSerializer.Deserialize<BpmnExecutionState>(LegacyJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Empty(direct.ActiveWork);
        Assert.Null(Assert.Single(direct.Tokens).ProducingWorkHandle);

        var read = new BpmnStateCodec().Read(Document(LegacyJson));

        Assert.Equal(1, read.Version);
        Assert.Equal("token:7", Assert.Single(read.State.ActiveWork).TokenId);
        Assert.Equal("iteration:2", Assert.Single(read.State.ActiveWork).IterationId);
        Assert.Equal("child:producer", Assert.Single(read.State.Tokens).ProducingWorkHandle);
        Assert.Equal("original.fault", read.State.PendingFault!.FaultCode);
        Assert.Equal("original message", read.State.PendingFault.Message);
    }

    [Fact]
    public void Continuing_v1_keeps_its_wire_names_and_version_and_retains_additive_stopped_identity()
    {
        var codec = new BpmnStateCodec();
        var original = codec.Read(Document(LegacyJson));
        var state = original.State with { Tokens = original.State.Tokens.Select(token => token with { StoppedHandle = "exact-child" }).ToArray() };

        var written = codec.Write(original, state);

        Assert.Equal(1, written.Type.SchemaVersion);
        var json = written.InlineValue!.Value;
        Assert.True(json.TryGetProperty("activeChildren", out _));
        Assert.False(json.TryGetProperty("activeWork", out _));
        Assert.Equal("child:producer", json.GetProperty("tokens")[0].GetProperty("producingActivityExecutionId").GetString());
        var reread = codec.Read(new ActivityPrivateState("parent", 1, written, "attempt", DateTimeOffset.UnixEpoch));
        Assert.Equal("exact-child", Assert.Single(reread.State.Tokens).StoppedHandle);
    }

    [Fact]
    public void Fresh_state_uses_new_version_and_scope_correlation_is_not_callback_correlation()
    {
        var codec = new BpmnStateCodec();
        var fresh = codec.Read(null, new Dictionary<string, string> { ["scope-start"] = "original" });
        var written = codec.Write(fresh, fresh.State);
        var read = codec.Read(new ActivityPrivateState("parent", 2, written, "attempt", DateTimeOffset.UnixEpoch),
            new Dictionary<string, string> { ["callback"] = "must-not-replace-scope" });

        Assert.Equal(2, read.Version);
        Assert.Equal("original", read.InvocationCorrelation["scope-start"]);
        Assert.False(read.InvocationCorrelation.ContainsKey("callback"));
    }

    [Theory]
    [InlineData(3, BpmnStateCodec.StateAlias)]
    [InlineData(1, "wrong.alias")]
    public void Unsupported_envelopes_are_refused_without_initializing_a_new_execution(int version, string alias) =>
        Assert.Throws<BpmnHostException>(() => new BpmnStateCodec().Read(Document(LegacyJson, version, alias)));

    [Fact]
    public void Missing_legacy_work_and_conflicting_aliases_are_refused()
    {
        var codec = new BpmnStateCodec();
        Assert.Throws<BpmnHostException>(() => codec.Read(Document("{\"tokens\":[]}")));
        Assert.Throws<BpmnHostException>(() => codec.Read(Document("{\"tokens\":[],\"activeChildren\":[],\"activeWork\":[]}")));
    }

    [Theory]
    [InlineData(1, "{\"activeChildren\":[]}")]
    [InlineData(2, "{}")]
    public void Truncated_checkpoint_is_not_a_fresh_empty_execution(int version, string json) =>
        Assert.Throws<BpmnHostException>(() => new BpmnStateCodec().Read(Document(json, version)));

    private static ActivityPrivateState Document(string json, int version = 1, string alias = BpmnStateCodec.StateAlias) =>
        new("parent", version, ValueEnvelope.Inline(new ValueTypeDescriptor(alias, schemaVersion: version),
            JsonDocument.Parse(json).RootElement.Clone(), ValueProtectionPolicy.InstanceInline), "attempt", DateTimeOffset.UnixEpoch);
}
