using System.Text.Json;
using Bpmn.Model.State;
using Elsa.Activities.Bpmn.Internal;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnStateCodecTests
{
    private const string RawLoopObject = """{ "escaped" : "\u0061\/\u003c", "number" : 12345678901234567890123456789012345, "fraction" : 1.0, "nested" : [ null, { "CaseKey" : "v" } ] }""";
    private const string RawLoopItems = "[ " + RawLoopObject + ", null, 1.0, 12345678901234567890123456789012345 ]";

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Reading_and_cold_rereading_loop_snapshots_preserves_exact_item_JSON(int version)
    {
        var original = Document(LoopCheckpoint(version), version);
        var originalJson = original.Value.InlineValue!.Value.GetRawText();
        var codec = new BpmnStateCodec();

        var read = codec.Read(original, new Dictionary<string, string> { ["callback"] = "must-not-replace-scope" });

        AssertRawLoopItems(read.State);
        Assert.Equal("child:producer", Assert.Single(read.State.Tokens).ProducingWorkHandle);
        Assert.Equal("node-work", Assert.Single(read.State.ActiveWork).NodeId);
        Assert.Equal("first", read.InvocationCorrelation["Event"]);
        Assert.Equal("second", read.InvocationCorrelation["event"]);
        Assert.False(read.InvocationCorrelation.ContainsKey("callback"));
        var written = codec.Write(read, read.State);
        Assert.Equal(version, written.Type.SchemaVersion);
        AssertRawLoopPayload(written.InlineValue!.Value, version);
        var cold = new BpmnStateCodec().Read(Document(written.InlineValue.Value.GetRawText(), version));
        AssertRawLoopItems(cold.State);
        Assert.Equal(version, cold.Version);
        Assert.Equal("child:producer", Assert.Single(cold.State.Tokens).ProducingWorkHandle);
        Assert.Equal("iteration:2", Assert.Single(cold.State.ActiveWork).IterationId);
        Assert.Equal("original.fault", cold.State.PendingFault!.FaultCode);
        Assert.Equal(originalJson, original.Value.InlineValue.Value.GetRawText());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Writing_typed_loop_snapshots_preserves_each_item_without_a_prior_read(int version)
    {
        using var items = JsonDocument.Parse(RawLoopItems);
        var rawItems = items.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
        var state = new BpmnExecutionState(tokens: [new BpmnToken("loop-token", "collection")],
            loops: [new BpmnLoopState("loop", "loop-token", "collection", true, rawItems.Length, 1, 0, rawItems)]);
        var document = new BpmnStateDocument(state, version, new Dictionary<string, string> { ["Event"] = "first", ["event"] = "second" });

        var written = new BpmnStateCodec().Write(document, state);

        AssertRawLoopPayload(written.InlineValue!.Value, version);
        Assert.Equal("first", written.InlineValue.Value.GetProperty("invocationCorrelation").GetProperty("Event").GetString());
        Assert.Equal("second", written.InlineValue.Value.GetProperty("invocationCorrelation").GetProperty("event").GetString());
        AssertRawLoopItems(new BpmnStateCodec().Read(Document(written.InlineValue.Value.GetRawText(), version)).State);
        Assert.Equal(RawLoopObject, rawItems[0].GetRawText());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Continuing_state_records_new_identity_without_rewriting_snapshotted_loop_items(int version)
    {
        var source = Document(LoopCheckpoint(version), version);
        var originalJson = source.Value.InlineValue!.Value.GetRawText();
        var codec = new BpmnStateCodec();
        var document = codec.Read(source);
        var progressed = document.State with
        {
            Tokens = document.State.Tokens.Select(token => token with { StoppedHandle = "new-stopped-child" }).ToArray()
        };

        var written = codec.Write(document, progressed);
        var cold = new BpmnStateCodec().Read(Document(written.InlineValue!.Value.GetRawText(), version));

        Assert.Equal("new-stopped-child", Assert.Single(cold.State.Tokens).StoppedHandle);
        Assert.Null(Assert.Single(document.State.Tokens).StoppedHandle);
        Assert.Equal("child:producer", Assert.Single(cold.State.Tokens).ProducingWorkHandle);
        AssertRawLoopItems(cold.State);
        AssertRawLoopPayload(written.InlineValue.Value, version);
        Assert.Equal(originalJson, source.Value.InlineValue.Value.GetRawText());
    }

    private static void AssertRawLoopPayload(JsonElement payload, int version)
    {
        Assert.True(payload.TryGetProperty(version == 1 ? "activeChildren" : "activeWork", out _));
        Assert.False(payload.TryGetProperty(version == 1 ? "activeWork" : "activeChildren", out _));
        var token = payload.GetProperty("tokens")[0];
        Assert.True(token.TryGetProperty(version == 1 ? "producingActivityExecutionId" : "producingWorkHandle", out _));
        Assert.False(token.TryGetProperty(version == 1 ? "producingWorkHandle" : "producingActivityExecutionId", out _));
        AssertRawLoopValues(payload.GetProperty("loops")[0].GetProperty("items").EnumerateArray());
    }

    private static void AssertRawLoopItems(BpmnExecutionState state) => AssertRawLoopValues(Assert.Single(state.Loops).Items!);

    private static void AssertRawLoopValues(IEnumerable<JsonElement> values)
    {
        var items = values.ToArray();
        Assert.Equal(4, items.Length);
        Assert.Equal(RawLoopObject, items[0].GetRawText());
        Assert.Equal(JsonValueKind.Null, items[1].ValueKind);
        Assert.Equal("1.0", items[2].GetRawText());
        Assert.Equal("12345678901234567890123456789012345", items[3].GetRawText());
    }

    private static string LoopCheckpoint(int version)
    {
        var loop = $$$"""[{"loopId":"loop","tokenId":"token:7","elementId":"work","isSequential":true,"totalCount":4,"nextIndex":1,"completedCount":0,"items":{{{RawLoopItems}}}}]""";
        var json = LegacyJson.Replace("\"loops\":[]", "\"loops\":" + loop, StringComparison.Ordinal)
            .Replace("\"kind\":null}", "\"kind\":null,\"stoppedHandle\":null}", StringComparison.Ordinal).TrimEnd();
        json = json[..^1] + ",\"invocationCorrelation\":{\"Event\":\"first\",\"event\":\"second\"}}";
        return version == 1 ? json : json.Replace("activeChildren", "activeWork", StringComparison.Ordinal)
            .Replace("producingActivityExecutionId", "producingWorkHandle", StringComparison.Ordinal);
    }
}
