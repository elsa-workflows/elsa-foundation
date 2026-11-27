using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Durable inbox shape only; no source authorization, callback execution or host recovery is implied.</summary>
public sealed class RuntimeChildControlInboxContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");
    private static readonly RuntimeChildControlTarget Target = new("parent", "call", "consumer", Pin, "root", "protocol", 1);

    [Fact]
    public void A_future_terminal_barrier_round_trips_without_advancing_the_cursor_or_losing_its_gap()
    {
        var state = new RuntimeChildControlInbox(1, Target, 0, false, [Transport(2, true), Transport(1)]);
        var restored = JsonSerializer.Deserialize<RuntimeChildControlInbox>(JsonSerializer.Serialize(state, Web), Web)!;
        Assert.Equal(0, restored.NextOrdinal);
        Assert.False(restored.IsTerminalConsumed);
        Assert.Equal([1L, 2L], restored.Pending.Select(item => item.Batch.Ordinal));
        Assert.True(restored.Pending[1].Batch.IsTerminal);
        Assert.All(restored.Pending, item => Assert.True(restored.Target.MatchesAddress(item)));
        Assert.Equal(state.Pending[1].SourceOutboxItemId, restored.Pending[1].SourceOutboxItemId);
    }

    [Theory]
    [InlineData("nextOrdinal")]
    [InlineData("isTerminalConsumed")]
    [InlineData("pending")]
    public void A_present_inbox_cannot_reset_a_truncated_cursor_or_discard_pending_control(string field)
    {
        var wire = JsonSerializer.SerializeToNode(new RuntimeChildControlInbox(1, Target, 1, false, [Transport(2, true)]), Web)!.AsObject();
        Assert.True(wire.Remove(field));
        Assert.Throws<JsonException>(() => wire.Deserialize<RuntimeChildControlInbox>(Web));
        wire[field] = null;
        Assert.Throws<JsonException>(() => wire.Deserialize<RuntimeChildControlInbox>(Web));
    }

    [Theory]
    [InlineData("unknown-version")]
    [InlineData("negative-cursor")]
    [InlineData("foreign-call")]
    [InlineData("foreign-consumer")]
    [InlineData("foreign-root")]
    [InlineData("foreign-pin")]
    [InlineData("duplicate")]
    [InlineData("before-cursor")]
    [InlineData("past-terminal")]
    [InlineData("multiple-terminal")]
    [InlineData("consumed-pending")]
    [InlineData("consumed-initial")]
    [InlineData("null-item")]
    public void Malformed_or_conflicting_durable_state_refuses_before_becoming_an_inbox(string defect)
    {
        var pending = defect switch
        {
            "foreign-call" => new[] { Transport(1, call: "foreign-call") },
            "foreign-consumer" => [Transport(1, consumer: "foreign-consumer")],
            "foreign-root" => [Transport(1, root: "foreign-root")],
            "foreign-pin" => [Transport(1, pin: Pin with { ArtifactHash = "foreign-hash" })],
            "duplicate" => [Transport(1), Transport(1)],
            "before-cursor" => [Transport(0)],
            "past-terminal" => [Transport(1, true), Transport(2)],
            "multiple-terminal" => [Transport(1, true), Transport(2, true)],
            "null-item" => [null!],
            "consumed-initial" => [],
            _ => [Transport(1)]
        };
        Assert.ThrowsAny<ArgumentException>(() => new RuntimeChildControlInbox(defect == "unknown-version" ? 2 : 1, Target,
            defect == "negative-cursor" ? -1 : defect == "consumed-initial" ? 0 : 1,
            defect is "consumed-pending" or "consumed-initial", pending));
    }

    [Fact]
    public void Pending_input_is_snapshotted_and_an_empty_initial_inbox_writes_its_explicit_collection()
    {
        var pending = new List<RuntimeChildControlTransport> { Transport(2, true) };
        var state = new RuntimeChildControlInbox(1, Target, 0, false, pending);
        pending.Clear();
        Assert.Single(state.Pending);
        var empty = JsonSerializer.Deserialize<RuntimeChildControlInbox>(JsonSerializer.Serialize(new RuntimeChildControlInbox(1, Target, 0, false), Web), Web)!;
        Assert.Empty(empty.Pending);
        Assert.Equal(0, empty.NextOrdinal);
    }

    [Fact]
    public void A_sealed_maximum_terminal_cursor_round_trips_without_overflow()
    {
        var state = new RuntimeChildControlInbox(1, Target, long.MaxValue, true);
        var restored = JsonSerializer.Deserialize<RuntimeChildControlInbox>(JsonSerializer.Serialize(state, Web), Web)!;
        Assert.True(restored.IsTerminalConsumed);
        Assert.Equal(long.MaxValue, restored.NextOrdinal);
        Assert.Empty(restored.Pending);
    }

    private static RuntimeChildControlTransport Transport(long ordinal, bool terminal = false, string call = "call",
        string consumer = "consumer", string root = "root", WorkflowExecutableIdentity? pin = null)
    {
        var identity = new WorkflowDispatchIdentity("parent", call);
        var commit = "source-commit-" + ordinal;
        var intent = "source-intent-" + ordinal;
        return new RuntimeChildControlTransport(identity.DispatchId, "parent", call, consumer, pin ?? Pin, commit, intent,
            RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit, intent),
            new RuntimeChildControlBatch(1, "protocol", 1, identity.ChildWorkflowExecutionId, root, "source-work-" + ordinal,
                ordinal, terminal, terminal ? [] : [new("notice")]));
    }
}
