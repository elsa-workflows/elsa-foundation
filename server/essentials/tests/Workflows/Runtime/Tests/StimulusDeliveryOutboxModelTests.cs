using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class StimulusDeliveryOutboxModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 1, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly RuntimePostCommitIntent Intent = new("send", "publisher", "publish", Now, null, "key", null);

    [Fact]
    public void Legacy_wire_stays_additive_free_and_original_CLR_constructor_remains()
    {
        var legacy = new RuntimePostCommitOutboxItem("commit:send", Intent, RuntimePostCommitOutboxStatus.Pending, Now, Now);
        var json = JsonSerializer.Serialize(legacy, Web);
        Assert.DoesNotContain("recipientSnapshot", json);
        Assert.DoesNotContain("stimulusDeliveryProtocolVersion", json);
        var restored = JsonSerializer.Deserialize<RuntimePostCommitOutboxItem>(json, Web)!;
        Assert.True(legacy.IsEquivalentTo(restored));
        Assert.Null(restored.RecipientSnapshot); Assert.Null(restored.StimulusDeliveryProtocolVersion);
        Assert.Contains(typeof(RuntimePostCommitOutboxItem).GetConstructors(), constructor => constructor.GetParameters().Length == 14);
    }

    [Fact]
    public void Protocol_without_selection_and_present_empty_cohort_are_distinct_in_wire_and_equality()
    {
        var unselected = Item(1, null);
        var selected = Item(1, Snapshot());
        Assert.False(unselected.IsEquivalentTo(selected));
        Assert.False(unselected.IsEquivalentPendingItem(selected));
        var unselectedWire = JsonSerializer.Serialize(unselected, Web);
        Assert.Contains("\"stimulusDeliveryProtocolVersion\":1", unselectedWire);
        Assert.DoesNotContain("recipientSnapshot", unselectedWire);
        var roundTrip = JsonSerializer.Deserialize<RuntimePostCommitOutboxItem>(JsonSerializer.Serialize(selected, Web), Web)!;
        Assert.NotNull(roundTrip.RecipientSnapshot);
        Assert.Empty(roundTrip.RecipientSnapshot.Resumes);
        Assert.True(selected.IsEquivalentTo(roundTrip));
    }

    [Theory]
    [InlineData(null, false)] [InlineData(0, false)] [InlineData(2, false)]
    [InlineData(1, true)]
    public void Cohort_cannot_hide_under_legacy_or_unknown_protocol_or_another_source(int? version, bool foreign)
    {
        Assert.ThrowsAny<ArgumentException>(() => Item(version, Snapshot(foreign ? "another:send" : "commit:send")));
    }

    [Fact]
    public void Equal_source_with_different_recipient_metadata_or_wait_generation_is_not_equivalent()
    {
        var first = Snapshot(starts: [Start("original")], resumes: [Wait(Now)]);
        var equivalent = JsonSerializer.Deserialize<StimulusDeliverySnapshot>(JsonSerializer.Serialize(first, Web), Web)!;
        Assert.True(first.IsEquivalentTo(equivalent));
        Assert.False(first.IsEquivalentTo(Snapshot(starts: [Start("changed")], resumes: [Wait(Now)])));
        Assert.False(first.IsEquivalentTo(Snapshot(starts: [Start("original")], resumes: [Wait(Now.AddTicks(1))])));
        Assert.False(first.IsEquivalentTo(null));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Generic_redrive_preserves_empty_or_populated_cohort_and_fences_the_old_claim(bool populated)
    {
        // This is the shared model transition, not an exposed PublishStimulus redrive API.
        var data = populated ? Snapshot(starts: [Start("saved")], resumes: [Wait(Now)]) : Snapshot();
        // Constructed witness data tests preservation only; it grants no runtime admission authority.
        var snapshot = new StimulusDeliverySnapshot(data.Version, data.SourceOutboxItemId, data.IntentFingerprint,
            data.Scope, data.SelectedAt, data.StimulusType, data.StimulusHash, data.Starts, data.Resumes, 1);
        var original = Item(1, snapshot);
        var claim = RuntimePostCommitOutboxClaimTransitions.Claim(original, new("first", Now, TimeSpan.FromMinutes(1), 1));
        var failed = RuntimePostCommitOutboxClaimTransitions.Complete(claim.Item, claim,
            new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.FailedFinal, Now.AddSeconds(1), "constructed model failure"));
        var redriven = RuntimePostCommitOutboxClaimTransitions.RedriveFailedFinal(failed, Now.AddSeconds(2));

        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, redriven.Status);
        Assert.Equal(failed.OutboxItemId, redriven.OutboxItemId);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIntent(failed.Intent), RuntimeCheckpointCommitFingerprint.ComputeIntent(redriven.Intent));
        Assert.Equal(failed.StimulusDeliveryProtocolVersion, redriven.StimulusDeliveryProtocolVersion);
        Assert.Equal(failed.StimulusDeliverySourceScope, redriven.StimulusDeliverySourceScope);
        Assert.True(snapshot.IsEquivalentTo(redriven.RecipientSnapshot));
        Assert.Equal(failed.DeliveryFencingToken + 1, redriven.DeliveryFencingToken);
        Assert.Equal(0, redriven.DeliveryAttemptCount);
        Assert.Null(redriven.DeliveringOwnerId);
        Assert.Null(redriven.DeliveryVisibleAfter);
        Assert.Throws<Elsa.Workflows.Runtime.Core.Exceptions.RuntimePostCommitOutboxStaleClaimException>(() =>
            RuntimePostCommitOutboxClaimTransitions.Complete(redriven, claim,
                new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(3))));
        Assert.True(snapshot.IsEquivalentTo(redriven.RecipientSnapshot));

        var next = RuntimePostCommitOutboxClaimTransitions.Claim(redriven, new("second", Now.AddSeconds(3), TimeSpan.FromMinutes(1), 1));
        var completed = RuntimePostCommitOutboxClaimTransitions.Complete(next.Item, next,
            new(next.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(4)));
        Assert.True(snapshot.IsEquivalentTo(completed.RecipientSnapshot));
    }

    private static RuntimePostCommitOutboxItem Item(int? protocol, StimulusDeliverySnapshot? snapshot) => new("commit:send", Intent,
        RuntimePostCommitOutboxStatus.Pending, Now, Now, null, 0, null, null, null, null, null, 0, null, protocol, protocol is null ? null : new("tenant"), snapshot);
    private static StimulusDeliverySnapshot Snapshot(string source = "commit:send", IReadOnlyList<StimulusStartRecipient>? starts = null,
        IReadOnlyList<StimulusResumeRecipient>? resumes = null) => new(1, source, RuntimeCheckpointCommitFingerprint.ComputeIntent(Intent),
        new(new("tenant"), null, null, WorkflowRunKind.Unknown, null), Now, "event", "named", starts ?? [], resumes ?? []);
    private static StimulusStartRecipient Start(string metadata) => new(new("binding", "artifact", "definition", "1.0.0", "hash",
        "node", "event", "named", null, new Dictionary<string, string> { ["key"] = metadata }, Now),
        new("artifact", "definition", "version", "1.0.0", "hash"));
    private static StimulusResumeRecipient Wait(DateTimeOffset created) => new("waiter", "bookmark", "activity", "node", "resume",
        "event", "named", created, null);
}
