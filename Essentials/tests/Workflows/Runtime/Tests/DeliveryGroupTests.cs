using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Internal accounting invariants; these tests do not qualify persistence, source authority or a chart selector.</summary>
public sealed class DeliveryGroupTests
{
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string OtherFingerprint = new('b', 64);
    private static readonly DeliveryGroupIdentity Key = new(new("scope"), "tenant", new("partition"), "workflow",
        "receiver", new("artifact", "definition", "version", "1", "hash"), "receiver-protocol", 1,
        "publication", 1, "source-A", Fingerprint);
    private static readonly DeliveryWorkReference Child = Work("child-resume", "child");
    private static readonly DeliveryWorkReference Parent = Work("parent-resume", "parent");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_callback_orders_keep_the_same_complete_ready_set(bool childFirst)
    {
        var group = New().Queue(Key, Child).Queue(Key, Parent).Apply(Key, Child).Apply(Key, Parent);
        var first = childFirst ? Child : Parent;
        var second = childFirst ? Parent : Child;
        group = Complete(group, first).Seal(Key, Fingerprint, OtherFingerprint);
        Assert.Single(group.Ready);
        Assert.False(group.CanClose);
        Assert.Throws<InvalidOperationException>(() => group.Close(Key));
        group = Complete(group, second).Close(Key);
        Assert.Equal(DeliveryGroupStatus.ClosedReady, group.Status);
        Assert.Equal(["child-ready", "parent-ready"], group.Ready.Select(item => item.OccurrenceId));
    }

    [Fact]
    public void A_seal_before_target_registration_cannot_discharge_the_missing_child()
    {
        var group = Complete(New().Seal(Key, Fingerprint, OtherFingerprint).Queue(Key, Parent).Apply(Key, Parent), Parent);
        Assert.Equal(0, group.RunnableCount);
        Assert.Equal(DeliveryRecipientStatus.Expected, group.RecipientStatus("child"));
        Assert.False(group.CanClose);
        Assert.Throws<InvalidOperationException>(() => group.Close(Key));
        group = Complete(group.Queue(Key, Child).Apply(Key, Child), Child).Close(Key);
        Assert.Equal(2, group.Ready.Count);
    }

    [Fact]
    public void A_queued_child_fault_before_consumption_blocks_parent_fallback_until_real_recovery()
    {
        var group = Complete(New().Queue(Key, Parent).Apply(Key, Parent), Parent)
            .Queue(Key, Child).Block(Key, Child, "child-read-failed", 1).Seal(Key, Fingerprint, OtherFingerprint);
        Assert.Equal(DeliveryGroupStatus.Blocked, group.Status);
        Assert.Equal(DeliveryRecipientStatus.Blocked, group.RecipientStatus("child"));
        Assert.False(group.CanClose);
        Assert.Throws<InvalidOperationException>(() => group.Apply(Key, Child));
        Assert.Throws<InvalidOperationException>(() => group.ResumeBlocked(Key, Child, "wrong-failure", 1));
        group = group.ResumeBlocked(Key, Child, "child-read-failed", 1).Apply(Key, Child);
        Assert.False(group.CanClose);
        group = Complete(group, Child).Close(Key);
        Assert.Equal(2, group.Ready.Count);
    }

    [Fact]
    public void Repair_and_old_failure_duplicates_do_not_revive_blocked_debt()
    {
        var group = New().Queue(Key, Child).Apply(Key, Child).Block(Key, Child, "failure-1", 1)
            .ResumeBlocked(Key, Child, "failure-1", 1);
        Assert.Same(group, group.Block(Key, Child, "failure-1", 1));
        Assert.Same(group, group.ResumeBlocked(Key, Child, "failure-1", 1));
        group = group.Block(Key, Child, "failure-2", 2);
        Assert.Same(group, group.ResumeBlocked(Key, Child, "failure-1", 1));
        Assert.Equal(DeliveryGroupStatus.Blocked, group.Status);
        group = group.ResumeBlocked(Key, Child, "failure-2", 2);
        Assert.Same(group, group.Block(Key, Child, "failure-1", 1));
        Assert.Same(group, group.ResumeBlocked(Key, Child, "failure-1", 1));
        Assert.Throws<InvalidOperationException>(() => group.Block(Key, Child, "different-failure-same-claim", 2));
        var laterFailure = group.Block(Key, Child, "failure-3", 3);
        Assert.Equal(DeliveryGroupStatus.Blocked, laterFailure.Status);
        group = Complete(group, Child);
        Assert.Same(group, group.ResumeBlocked(Key, Child, "failure-2", 2));
    }

    [Fact]
    public void Successor_transfer_has_no_observable_zero_debt_and_replay_does_not_reopen_it()
    {
        var successor = Work("child-completion", "child");
        var predecessor = New().Queue(Key, Child).Apply(Key, Child)
            .RetireBeforeConsumption(Key, "parent", "no-longer-waiting").Seal(Key, Fingerprint, OtherFingerprint);
        var transferred = predecessor.Decide(Key, Child, "resume-completed", DeliveryWorkStatus.Settled, [successor]);
        Assert.Equal(DeliveryWorkStatus.Runnable, predecessor.WorkStatus(Child.WorkId));
        Assert.Equal(DeliveryWorkStatus.Settled, transferred.WorkStatus(Child.WorkId));
        Assert.Equal(1, transferred.RunnableCount);
        Assert.False(transferred.CanClose);
        var completed = Complete(transferred, successor).Close(Key);
        Assert.Same(completed, completed.Decide(Key, Child, "resume-completed", DeliveryWorkStatus.Settled, [successor]));
        Assert.Same(completed, completed.Queue(Key, Child));
        Assert.Throws<InvalidOperationException>(() => completed.Decide(Key, Child, "changed", DeliveryWorkStatus.Settled));
    }

    [Fact]
    public void A_real_park_is_not_a_wait_for_the_whole_composite_lifetime()
    {
        var group = Complete(New().Queue(Key, Parent).Apply(Key, Parent), Parent).Queue(Key, Child).Apply(Key, Child)
            .Decide(Key, Child, "durable-wait-created", DeliveryWorkStatus.Parked)
            .Seal(Key, Fingerprint, OtherFingerprint).Close(Key);
        Assert.Equal(["parent-ready"], group.Ready.Select(item => item.OccurrenceId));
        Assert.Equal(DeliveryWorkStatus.Parked, group.WorkStatus(Child.WorkId));

        var laterKey = Key with { SourceId = "source-B", SourceFingerprint = OtherFingerprint };
        Assert.Throws<InvalidOperationException>(() => group.Queue(laterKey, Work("later-child", "child")));
        var later = new DeliveryGroup(laterKey, OtherFingerprint, [Recipient("child")], 8);
        var laterWork = Work("later-child", "child");
        later = later.Queue(laterKey, laterWork).Apply(laterKey, laterWork)
            .Decide(laterKey, laterWork, "later-completion", DeliveryWorkStatus.Settled,
                ready: [Ready("child", "retained-result-A")])
            .Seal(laterKey, OtherFingerprint, Fingerprint).Close(laterKey);
        Assert.Equal("source-B", later.Identity.SourceId);
        Assert.Equal("retained-result-A", Assert.Single(later.Ready).ResultReferenceId);
        Assert.Equal("source-A", group.Identity.SourceId);
    }

    [Fact]
    public void Definitive_retirement_can_settle_a_failed_unconsumed_wait_but_not_erase_applied_work()
    {
        var group = Complete(New().Queue(Key, Parent).Apply(Key, Parent), Parent)
            .Queue(Key, Child).Block(Key, Child, "read-failed", 1);
        group = group.RetireBeforeConsumption(Key, "child", "exact-wait-retired")
            .Seal(Key, Fingerprint, OtherFingerprint).Close(Key);
        Assert.Equal(DeliveryRecipientStatus.Retired, group.RecipientStatus("child"));
        Assert.Equal(DeliveryWorkStatus.Retired, group.WorkStatus(Child.WorkId));
        Assert.Single(group.Ready);
        Assert.Same(group, group.Queue(Key, Child));
        Assert.Throws<InvalidOperationException>(() => group.RetireBeforeConsumption(Key, "child", "different-retirement"));

        var applied = New().Queue(Key, Child).Apply(Key, Child);
        Assert.Throws<InvalidOperationException>(() => applied.RetireBeforeConsumption(Key, "child", "pretend-never-consumed"));
    }

    [Fact]
    public void Neither_an_empty_queue_nor_queued_acceptance_substitutes_for_consumption_or_seal()
    {
        var group = New().Queue(Key, Child).Queue(Key, Parent);
        Assert.Throws<InvalidOperationException>(() => Complete(group, Child));
        group = Complete(Complete(group.Apply(Key, Child).Apply(Key, Parent), Child), Parent);
        Assert.Equal(0, group.RunnableCount);
        Assert.False(group.CanClose);
        Assert.Throws<InvalidOperationException>(() => group.Close(Key));
        Assert.True(group.Seal(Key, Fingerprint, OtherFingerprint).CanClose);
    }

    [Fact]
    public void Conflicting_manifests_work_and_visit_results_refuse_without_mutating_the_prior_state()
    {
        var group = New().Queue(Key, Child).Apply(Key, Child);
        Assert.Throws<InvalidOperationException>(() => group.Seal(Key, OtherFingerprint, Fingerprint));
        Assert.Throws<InvalidOperationException>(() => group.Queue(Key, Child with { Fingerprint = OtherFingerprint }));
        Assert.Throws<InvalidOperationException>(() => group.Queue(Key, Work("alien", "unknown-slot")));
        Assert.Throws<InvalidOperationException>(() => group.Decide(Key, Child, "wrong-visit", DeliveryWorkStatus.Settled,
            ready: [Ready("child") with { VisitId = "replacement-visit" }]));
        Assert.Throws<InvalidOperationException>(() => group.Decide(Key, Child, "wrong-operation", DeliveryWorkStatus.Settled,
            ready: [Ready("child") with { TriggerOperationId = "replacement-trigger" }]));
        Assert.Empty(group.Ready);
        Assert.Equal(DeliveryWorkStatus.Runnable, group.WorkStatus(Child.WorkId));
    }

    [Fact]
    public void One_owed_work_item_cannot_fork_into_an_existing_identity_or_another_recipient()
    {
        var group = New().Queue(Key, Child).Apply(Key, Child).Queue(Key, Parent).Apply(Key, Parent);
        Assert.Throws<InvalidOperationException>(() => group.Decide(Key, Child, "cycle", DeliveryWorkStatus.Settled, [Child]));
        Assert.Throws<InvalidOperationException>(() => group.Decide(Key, Child, "foreign-slot", DeliveryWorkStatus.Settled, [Work("next", "parent")]));
        Assert.Throws<InvalidOperationException>(() => group.Decide(Key, Child, "duplicate", DeliveryWorkStatus.Settled,
            [Work("next", "child"), Work("next", "child")]));
        Assert.Equal(2, group.RunnableCount);
    }

    [Fact]
    public void A_bounded_ledger_refuses_before_losing_replay_tombstones_or_existing_obligations()
    {
        var group = New(budget: 2).Queue(Key, Child).Apply(Key, Child).Queue(Key, Parent).Apply(Key, Parent);
        Assert.Throws<InvalidOperationException>(() => group.Decide(Key, Child, "more", DeliveryWorkStatus.Settled, [Work("next", "child")]));
        Assert.Equal(DeliveryWorkStatus.Runnable, group.WorkStatus(Child.WorkId));
        Assert.Equal(2, group.KnownWorkCount);
    }

    [Fact]
    public void Ended_consumer_disposition_keeps_evidence_without_creating_a_selection_or_new_work()
    {
        var group = Complete(New().Queue(Key, Parent).Apply(Key, Parent), Parent).Queue(Key, Child).Block(Key, Child, "failed", 1);
        var ended = group.EndConsumer(Key, "superseded-receiver");
        Assert.Equal(DeliveryGroupStatus.ConsumerEnded, ended.Status);
        Assert.Equal(DeliveryWorkStatus.Retired, ended.WorkStatus(Child.WorkId));
        Assert.Equal(DeliveryWorkStatus.Settled, ended.WorkStatus(Parent.WorkId));
        Assert.Single(ended.Ready);
        Assert.False(ended.CanClose);
        Assert.Same(ended, ended.Seal(Key, Fingerprint, OtherFingerprint));
        Assert.Same(ended, ended.Queue(Key, Child));
        Assert.Equal(DeliveryGroupStatus.Blocked, group.Status);
    }

    [Fact]
    public void A_later_terminal_sweep_does_not_rewrite_an_already_closed_group()
    {
        var group = New().RetireBeforeConsumption(Key, "child", "retired-child")
            .RetireBeforeConsumption(Key, "parent", "retired-parent")
            .Seal(Key, Fingerprint, OtherFingerprint).Close(Key);
        Assert.Same(group, group.EndConsumer(Key, "workflow-ended"));
        Assert.Equal(DeliveryGroupStatus.ClosedReady, group.Status);
        Assert.Empty(group.Ready);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("tenant")]
    [InlineData("partition")]
    [InlineData("workflow")]
    [InlineData("receiver")]
    [InlineData("material")]
    [InlineData("protocol")]
    [InlineData("source")]
    [InlineData("source-version")]
    [InlineData("source-content")]
    public void An_operation_cannot_retarget_another_exact_consumer_or_source(string difference)
    {
        var other = difference switch
        {
            "scope" => Key with { Scope = new("other") },
            "tenant" => Key with { TenantId = "other" },
            "partition" => Key with { Partition = new("other") },
            "workflow" => Key with { WorkflowExecutionId = "other" },
            "receiver" => Key with { ReceiverInvocationId = "other" },
            "material" => Key with { Executable = Key.Executable with { ArtifactHash = "other" } },
            "protocol" => Key with { ReceiverVersion = 2 },
            "source" => Key with { SourceId = "other" },
            "source-version" => Key with { SourceVersion = 2 },
            _ => Key with { SourceFingerprint = OtherFingerprint }
        };
        Assert.Throws<InvalidOperationException>(() => New().Queue(other, Child));
    }

    [Fact]
    public void Caller_collections_cannot_mutate_a_captured_manifest_or_committed_transfer()
    {
        var recipients = new List<DeliveryRecipient> { Recipient("child"), Recipient("parent") };
        var group = new DeliveryGroup(Key, Fingerprint, recipients, 8);
        recipients.Clear();
        Assert.Equal(DeliveryRecipientStatus.Expected, group.RecipientStatus("parent"));
        var next = new List<DeliveryWorkReference> { Work("next", "child") };
        group = group.Queue(Key, Child).Apply(Key, Child).Decide(Key, Child, "transfer", DeliveryWorkStatus.Settled, next);
        next.Clear();
        Assert.Equal(1, group.RunnableCount);
        Assert.Same(group, group.Decide(Key, Child, "transfer", DeliveryWorkStatus.Settled, [Work("next", "child")]));
    }

    private static DeliveryGroup New(int budget = 16) => new(Key, Fingerprint, [Recipient("child"), Recipient("parent")], budget);
    private static DeliveryRecipient Recipient(string slot) => new(slot, slot + "-activity", slot + "-wait", "generation-1", Fingerprint, slot + "-visit", slot + "-trigger");
    private static DeliveryWorkReference Work(string id, string slot) => new(id, slot, Fingerprint);
    private static DeliveryReadyOccurrence Ready(string slot, string? result = null) => new(slot + "-ready", slot, slot + "-visit", slot + "-trigger", slot + "-activation", slot + "-completion", result ?? slot + "-result");
    private static DeliveryGroup Complete(DeliveryGroup group, DeliveryWorkReference work) => group.Decide(group.Identity, work, work.WorkId + "-done", DeliveryWorkStatus.Settled, ready: [Ready(work.SlotId)]);
}
