using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Immutable supplied origins only; no semantic producer, publication/child handler or history module enabled.</summary>
public sealed class RuntimeHistoryPostCommitOriginTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCausation Origin = new("event", "run", "commit:send", "command", "work");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Origin_roundtrips_while_absence_keeps_the_legacy_null_omitted_wire_shape(bool web)
    {
        var options = web ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions();
        var absent = Intent(null);
        var present = Intent(Origin);
        Assert.False(JsonSerializer.SerializeToElement(absent, options).TryGetProperty(web ? "historyCausation" : "HistoryCausation", out _));
        var restored = JsonSerializer.Deserialize<RuntimePostCommitIntent>(JsonSerializer.Serialize(present, options), options)!;
        Assert.Equal(Origin, restored.HistoryCausation);
        Assert.True(present.IsEquivalentTo(restored));
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIntent(present), RuntimeCheckpointCommitFingerprint.ComputeIntent(restored));
        Assert.False(absent.IsEquivalentTo(present));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.ComputeIntent(absent), RuntimeCheckpointCommitFingerprint.ComputeIntent(present));
    }

    [Fact]
    public void The_pre_history_checkpoint_and_source_intent_formats_remain_unchanged()
    {
        var legacy = new RuntimeCheckpointCommit("commit", new("checkpoint", "name", "waiter", At, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>());
        // Existing compiled-parent golden shared by the history-free checkpoint/owner contract controls.
        Assert.Equal("af976cb76bbef3f226bc15cbbdae5d8b330ec98ae9f287ff18e15aadc2df1f1a", RuntimeCheckpointCommitFingerprint.Compute(legacy));
        // Frozen v1 byte fixture: length-framed UTF-16 fields, UTC ticks639285696000000000,
        // absent activity/key/dependency/wait/not-before/payload, metadata count0. Not a new format oracle.
        var legacyIntent = new RuntimePostCommitIntent("legacy", "run", "test.send", At, null, null, null);
        Assert.Equal("d7e799f4a79243aa95562e7704a549670c080d687249db635c123e3aca9f4cfe", RuntimeCheckpointCommitFingerprint.ComputeIntent(legacyIntent));
    }

    [Fact]
    public void A_diagnostic_origin_does_not_grant_source_protocol_scope_or_a_recipient_cohort()
    {
        var intent = Intent(Origin);
        var item = RuntimePostCommitOutboxItems.CreatePending(Envelope(intent), intent);
        Assert.Equal(Origin, item.Intent.HistoryCausation);
        Assert.Null(item.StimulusDeliveryProtocolVersion);
        Assert.Null(item.StimulusDeliverySourceScope);
        Assert.Null(item.RecipientSnapshot);
    }

    [Theory]
    [InlineData("event", false)]
    [InlineData("run", false)]
    [InlineData("delivery", false)]
    [InlineData("command", false)]
    [InlineData("work", false)]
    [InlineData("event", true)]
    [InlineData("run", true)]
    [InlineData("delivery", true)]
    [InlineData("command", true)]
    [InlineData("work", true)]
    public void Every_original_causal_field_binds_intent_and_checkpoint_identity_with_exact_opaque_code_units(string field, bool opaque)
    {
        var first = Intent(Change(field, opaque ? "\ud800" : "first"));
        var second = Intent(Change(field, opaque ? "\ufffd" : "second"));
        Assert.False(first.IsEquivalentTo(second));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.ComputeIntent(first), RuntimeCheckpointCommitFingerprint.ComputeIntent(second));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(Complete(first)), RuntimeCheckpointCommitFingerprint.Compute(Complete(second)));
    }

    [Fact]
    public void A_pending_projection_cannot_disagree_with_its_original_intent_origin()
    {
        var intent = Intent(Origin);
        var valid = Complete(intent);
        RuntimeCheckpointCommitValidator.Validate(valid);
        var foreign = RuntimePostCommitOutboxItems.CreatePending(valid, Intent(Origin with { HistoryEventId = "another-event" }));
        var mismatch = valid with { StateChanges = valid.StateChanges.WithPostCommitOutbox([
            new(foreign.OutboxItemId, RuntimeStateChangeOperation.Upsert, foreign, valid.Metadata)]) };
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(valid), RuntimeCheckpointCommitFingerprint.Compute(mismatch));
        Assert.Throws<RuntimeCheckpointCommitValidationException>(() => RuntimeCheckpointCommitValidator.Validate(mismatch));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Raw_wire_and_actual_memory_checkpoint_replay_keep_the_origin_and_refuse_rebinding(bool web)
    {
        var options = web ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions();
        var original = Complete(Intent(Origin));
        var retained = new RetainedOriginInput(original.CommitId, original.Checkpoint, Assert.Single(original.PostCommitIntents),
            Assert.Single(original.StateChanges.PostCommitOutbox).State, original.Metadata);
        var restored = JsonSerializer.Deserialize<RetainedOriginInput>(JsonSerializer.Serialize(retained, options), options)!.Reconstruct();
        RuntimeCheckpointCommitValidator.Validate(restored);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(original), RuntimeCheckpointCommitFingerprint.Compute(restored));
        var state = new InMemoryRuntimeCheckpointStoreState();
        var store = new InMemoryRuntimeCheckpointCommitStore(state: state);
        var immediate = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        await store.CommitAsync(restored, immediate);
        await store.CommitAsync(original, immediate);
        Assert.Single(store.ListCommits());
        Assert.Equal(Origin, (await store.FindAsync("commit:send"))!.Intent.HistoryCausation);
        Assert.Empty(state.HistoryHeads); // Carrying an origin alone neither enables capture nor creates a history owner.
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(async () =>
            await store.CommitAsync(Complete(Intent(Origin with { HistoryEventId = "rebound" })), immediate));
        Assert.Equal(Origin, (await store.FindAsync("commit:send"))!.Intent.HistoryCausation);
    }

    private static RuntimeHistoryCausation Change(string field, string value) => field switch
    {
        "event" => Origin with { HistoryEventId = value }, "run" => Origin with { WorkflowExecutionId = value },
        "delivery" => Origin with { DeliveryId = value }, "command" => Origin with { CommandId = value },
        "work" => Origin with { SchedulerWorkItemId = value }, _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
    private static RuntimePostCommitIntent Intent(RuntimeHistoryCausation? origin) =>
        new("send", "run", "test.send", At, "activity", "key", JsonSerializer.SerializeToElement(new { value = "retained" })) { HistoryCausation = origin };
    private static RuntimeCheckpointCommit Envelope(RuntimePostCommitIntent intent) =>
        new("commit", new("checkpoint", "name", "run", At, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [intent], new Dictionary<string, string>());
    private static RuntimeCheckpointCommit Complete(RuntimePostCommitIntent intent)
    {
        var envelope = Envelope(intent);
        return envelope with { StateChanges = envelope.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(envelope)) };
    }

    // The production checkpoint/change-set classes have no whole-object deserialization contract.
    // Retain their raw supported inputs, then reconstruct the actual native commit explicitly.
    private sealed record RetainedOriginInput(string CommitId, RuntimeCheckpoint Checkpoint, RuntimePostCommitIntent Intent,
        RuntimePostCommitOutboxItem Pending, IReadOnlyDictionary<string, string> Metadata)
    {
        internal RuntimeCheckpointCommit Reconstruct() => new(CommitId, Checkpoint,
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []).WithPostCommitOutbox([
                new(Pending.OutboxItemId, RuntimeStateChangeOperation.Upsert, Pending, Metadata)]), [Intent], Metadata);
    }
}
