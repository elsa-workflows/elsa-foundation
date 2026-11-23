using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class StepWorkflowStateEffectSerializationTests
{
    [Fact]
    public void Receipt_profile_reads_original_writer_and_original_reader_reads_current_writer()
    {
        var receipts = new[]
        {
            new StepWorkflowStateEffectReceipt(null, new(2026, 7, 9, 12, 0, 0, TimeSpan.FromHours(2)), "Completion", ["outputs"]),
            new StepWorkflowStateEffectReceipt("é<\"\\\n中", new(2026, 1, 9, 8, 30, 0, TimeSpan.FromHours(-5)), "Suspension", ["correlationId", "instanceName"])
        };
        // These optionless calls deliberately reproduce the original closed metadata protocol.
        var original = JsonSerializer.Serialize(receipts);
        var current = StepWorkflowStateEffects.WriteReceipts(receipts);
        AssertReceipts(receipts, StepWorkflowStateEffects.ReadReceipts(Metadata(original)));
        AssertReceipts(receipts, JsonSerializer.Deserialize<StepWorkflowStateEffectReceipt[]>(current)!);
        using var wire = JsonDocument.Parse(current);
        Assert.True(wire.RootElement[0].TryGetProperty("AttemptId", out var attempt));
        Assert.Equal(JsonValueKind.Null, attempt.ValueKind);
        Assert.False(wire.RootElement[0].TryGetProperty("attemptId", out _));
        Assert.Equal("2026-07-09T12:00:00+02:00", wire.RootElement[0].GetProperty("AcceptedAt").GetString());
    }

    [Fact]
    public void Appending_to_original_receipts_retains_previous_attribution_and_filters_only_unknown_kinds()
    {
        const string original = """[{"AttemptId":null,"AcceptedAt":"2026-07-09T12:00:00+02:00","Transition":"Completion","Kinds":["outputs","unknown","outputs"]}]""";
        var next = new StepWorkflowStateEffectReceipt("attempt-2", DateTimeOffset.UnixEpoch, "Suspension", ["instanceName"]);
        var appended = StepWorkflowStateEffects.WriteReceipts(StepWorkflowStateEffects.ReadReceipts(Metadata(original)).Append(next));
        var restored = StepWorkflowStateEffects.ReadReceipts(Metadata(appended)).ToArray();
        Assert.Equal(2, restored.Length);
        Assert.Null(restored[0].AttemptId);
        Assert.Equal(TimeSpan.FromHours(2), restored[0].AcceptedAt.Offset);
        Assert.Equal(["outputs"], restored[0].Kinds);
        AssertReceipts([next], [restored[1]]);
        AssertReceipts(restored, JsonSerializer.Deserialize<StepWorkflowStateEffectReceipt[]>(appended)!);
        Assert.Empty(StepWorkflowStateEffects.ReadReceipts(Metadata("not JSON")));
    }

    private static Dictionary<string, string> Metadata(string json) => new() { [StepWorkflowStateEffects.ReceiptsMetadataKey] = json };

    private static void AssertReceipts(IEnumerable<StepWorkflowStateEffectReceipt> expected, IEnumerable<StepWorkflowStateEffectReceipt> actual) =>
        Assert.Equal(expected.Select(Shape), actual.Select(Shape));

    private static object Shape(StepWorkflowStateEffectReceipt receipt) =>
        new { receipt.AttemptId, Ticks = receipt.AcceptedAt.Ticks, receipt.AcceptedAt.Offset, receipt.Transition, Kinds = string.Join('|', receipt.Kinds) };
}
