using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// R2 contract preparation only. These cases do not establish provider authority, hashing,
// durable install/claims/seals, native admission, retention or enable a routing producer.
public sealed class StimulusIngressCohortTests
{
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1", "hash");
    private static readonly StimulusDeliveryScope ReceiverScope = new(Scope, "tenant", new("partition"), WorkflowRunKind.PublishedRun, null);
    private static readonly DateTimeOffset Created = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Digest = new('a', 64);

    [Fact]
    public void A_tracked_wait_cannot_be_subtracted_to_turn_legacy_ambiguity_into_a_single_match()
    {
        var tracked = Wait("tracked");
        var ordinary = Wait("ordinary");
        var consumer = Consumer(tracked);
        var ambiguous = new StimulusIngressLegacyResolution("workflow", StimulusIngressLegacyMode.SingleMatch,
            StimulusIngressLegacyStatus.Ambiguous, [tracked.BookmarkId, ordinary.BookmarkId], []);
        var cohort = Cohort([tracked, ordinary], [consumer], [ambiguous]);

        Assert.Empty(Assert.Single(cohort.Legacy).BookmarkIdsToResume);
        Assert.Throws<ArgumentException>(() => new StimulusIngressLegacyResolution("workflow",
            StimulusIngressLegacyMode.SingleMatch, StimulusIngressLegacyStatus.Resolved,
            [tracked.BookmarkId, ordinary.BookmarkId], [ordinary.BookmarkId]));
    }

    [Fact]
    public void Legacy_ambiguity_keeps_matches_excluded_by_global_correlation()
    {
        var routed = Wait("correlated", "correlation");
        // The ordinary instance lookup has no correlation filter. Its other matching bookmark
        // must still make this ambiguous, although global routing selected only the first one.
        var ordinary = new StimulusIngressLegacyResolution("workflow", StimulusIngressLegacyMode.SingleMatch,
            StimulusIngressLegacyStatus.Ambiguous, [routed.BookmarkId, "other-correlation"], []);

        var cohort = Cohort([routed], [], [ordinary]);

        Assert.Single(cohort.Resumes);
        Assert.Equal(2, Assert.Single(cohort.Legacy).OriginalBookmarkIds.Count);
        Assert.Empty(cohort.Legacy[0].BookmarkIdsToResume);
        Assert.Same(cohort, Source(cohort, false, correlation: "correlation").Cohort);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_cardinality_cannot_silently_change_the_legacy_resolution(bool broadcast)
    {
        var tracked = Wait("tracked");
        var ordinary = Wait("ordinary");
        var legacy = new StimulusIngressLegacyResolution("workflow",
            broadcast ? StimulusIngressLegacyMode.SingleMatch : StimulusIngressLegacyMode.AllMatches,
            broadcast ? StimulusIngressLegacyStatus.Ambiguous : StimulusIngressLegacyStatus.Resolved,
            [tracked.BookmarkId, ordinary.BookmarkId], broadcast ? [] : [ordinary.BookmarkId]);
        var cohort = Cohort([tracked, ordinary], [Consumer(tracked)], [legacy]);

        Assert.Throws<ArgumentException>(() => Source(cohort, broadcast));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Matching_source_cardinality_preserves_the_original_legacy_decision(bool broadcast)
    {
        var tracked = Wait("tracked");
        var ordinary = Wait("ordinary");
        var legacy = new StimulusIngressLegacyResolution("workflow",
            broadcast ? StimulusIngressLegacyMode.AllMatches : StimulusIngressLegacyMode.SingleMatch,
            broadcast ? StimulusIngressLegacyStatus.Resolved : StimulusIngressLegacyStatus.Ambiguous,
            [tracked.BookmarkId, ordinary.BookmarkId], broadcast ? [ordinary.BookmarkId] : []);
        var cohort = Cohort([tracked, ordinary], [Consumer(tracked)], [legacy]);

        Assert.Same(cohort, Source(cohort, broadcast).Cohort);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other")]
    public void A_source_cannot_claim_a_routed_wait_outside_its_global_correlation(string? capturedCorrelation)
    {
        var wait = Wait("original", capturedCorrelation);
        var cohort = Cohort([wait], [Consumer(wait)], []);

        Assert.Throws<ArgumentException>(() => Source(cohort, false, correlation: "expected"));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("partition")]
    [InlineData("run")]
    [InlineData("test-scope")]
    public void An_empty_cohort_still_retains_the_full_origin_for_keyed_lookup(string difference)
    {
        var partition = new WorkflowExecutionPartition("partition");
        var expiry = Created.AddHours(1);
        var origin = new StimulusDeliveryScope(Scope, "tenant", partition, WorkflowRunKind.TestRun,
            new("first-test", expiry, "tenant", partition));
        var source = Source(Cohort([], [], []), false, origin: origin);
        var otherPartition = difference == "partition" ? new WorkflowExecutionPartition("other") : partition;
        var otherTenant = difference == "tenant" ? "other" : "tenant";
        var other = new StimulusDeliveryScope(Scope, otherTenant, otherPartition,
            difference == "run" ? WorkflowRunKind.PublishedRun : WorkflowRunKind.TestRun,
            difference == "run" ? null : new(difference == "test-scope" ? "second-test" : "first-test", expiry, otherTenant, otherPartition));

        Assert.Empty(source.Cohort.Resumes);
        Assert.Same(origin, source.OriginScope);
        Assert.True(source.MatchesOrigin(new(Scope, "tenant", partition, WorkflowRunKind.TestRun,
            new("first-test", expiry, "tenant", partition))));
        Assert.False(source.MatchesOrigin(other));
    }

    [Fact]
    public void Explicit_broadcast_can_dispatch_the_remaining_legacy_wait_without_changing_single_match_rules()
    {
        var tracked = Wait("tracked");
        var ordinary = Wait("ordinary");
        var legacy = new StimulusIngressLegacyResolution("workflow", StimulusIngressLegacyMode.AllMatches,
            StimulusIngressLegacyStatus.Resolved, [tracked.BookmarkId, ordinary.BookmarkId], [ordinary.BookmarkId]);

        var cohort = Cohort([tracked, ordinary], [Consumer(tracked)], [legacy]);

        Assert.Equal(ordinary.BookmarkId, Assert.Single(cohort.Legacy[0].BookmarkIdsToResume));
        Assert.Equal(tracked.BookmarkId, Assert.Single(cohort.Consumers[0].Slots).BookmarkId);
    }

    [Fact]
    public void An_execution_with_only_tracked_recipients_has_no_legacy_branch()
    {
        var wait = Wait("tracked");
        var consumer = Consumer(wait);
        Assert.Empty(Cohort([wait], [consumer], []).Legacy);

        var legacy = new StimulusIngressLegacyResolution("workflow", StimulusIngressLegacyMode.SingleMatch,
            StimulusIngressLegacyStatus.Resolved, [wait.BookmarkId], []);
        Assert.Throws<ArgumentException>(() => Cohort([wait], [consumer], [legacy]));
    }

    [Fact]
    public void Explicit_empty_selection_is_distinct_from_a_missing_or_unaccounted_recipient()
    {
        var empty = Cohort([], [], []);
        Assert.Empty(empty.Starts);
        Assert.Empty(empty.Resumes);
        Assert.Empty(empty.Consumers);
        Assert.Throws<ArgumentException>(() => Cohort([Wait("lost")], [], []));
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("activity")]
    [InlineData("material")]
    [InlineData("tenant")]
    [InlineData("partition")]
    public void Tracked_ownership_cannot_retarget_a_captured_wait(string difference)
    {
        var wait = Wait("original");
        var scope = difference switch
        {
            "tenant" => new StimulusDeliveryScope(Scope, "other", ReceiverScope.Partition, WorkflowRunKind.PublishedRun, null),
            "partition" => new StimulusDeliveryScope(Scope, "tenant", new("other"), WorkflowRunKind.PublishedRun, null),
            _ => ReceiverScope
        };
        var slot = new StimulusIngressSlot("slot", wait.BookmarkId,
            difference == "activity" ? "other" : wait.ActivityExecutionId,
            difference == "generation" ? 2 : 1, "visit", "trigger", "activation");
        var consumer = new StimulusIngressConsumer("workflow", "receiver",
            difference == "material" ? Pin with { ArtifactHash = "other" } : Pin, scope, "protocol", 1, [slot]);

        Assert.Throws<ArgumentException>(() => Cohort([wait], [consumer], []));
    }

    [Fact]
    public void One_original_wait_cannot_belong_to_two_unrelated_receiver_invocations()
    {
        var wait = Wait("original");
        var first = Consumer(wait);
        var second = new StimulusIngressConsumer("workflow", "other-receiver", Pin, ReceiverScope, "protocol", 1, first.Slots);

        Assert.Throws<ArgumentException>(() => Cohort([wait], [first, second], []));
    }

    [Fact]
    public void A_nested_trigger_preserves_both_receiver_obligations_for_one_physical_wait()
    {
        var wait = Wait("original");
        var outer = Consumer(wait);
        var inner = new StimulusIngressConsumer("workflow", "inner-receiver", Pin, ReceiverScope, "protocol", 1,
            [new("inner-slot", wait.BookmarkId, wait.ActivityExecutionId, 1, "inner-visit", "inner-trigger", "inner-activation")],
            parentInvocationId: outer.InvocationId);

        var cohort = Cohort([wait], [inner, outer], []);

        Assert.Single(cohort.Resumes);
        Assert.Equal(2, cohort.Consumers.Count);
        Assert.Empty(cohort.Legacy);
    }

    [Fact]
    public void A_cyclic_receiver_chain_cannot_claim_a_shared_wait()
    {
        var wait = Wait("original");
        var slots = Consumer(wait).Slots;
        var first = new StimulusIngressConsumer("workflow", "first", Pin, ReceiverScope, "protocol", 1, slots, "second");
        var second = new StimulusIngressConsumer("workflow", "second", Pin, ReceiverScope, "protocol", 1, slots, "first");

        Assert.Throws<ArgumentException>(() => Cohort([wait], [first, second], []));
    }

    [Fact]
    public void A_caller_cannot_change_a_captured_legacy_resolution_or_cohort_collection()
    {
        var wait = Wait("original");
        var original = new List<string> { wait.BookmarkId };
        var dispatch = new List<string> { wait.BookmarkId };
        var legacy = new StimulusIngressLegacyResolution("workflow", StimulusIngressLegacyMode.SingleMatch,
            StimulusIngressLegacyStatus.Resolved, original, dispatch);
        var resumes = new List<StimulusResumeRecipient> { wait };
        var resolutions = new List<StimulusIngressLegacyResolution> { legacy };
        var cohort = Cohort(resumes, [], resolutions);

        original.Clear();
        dispatch.Clear();
        resumes.Clear();
        resolutions.Clear();

        Assert.Equal(wait.BookmarkId, Assert.Single(cohort.Resumes).BookmarkId);
        Assert.Equal(wait.BookmarkId, Assert.Single(cohort.Legacy[0].OriginalBookmarkIds));
        Assert.Equal(wait.BookmarkId, Assert.Single(cohort.Legacy[0].BookmarkIdsToResume));
    }

    private static StimulusIngressCohort Cohort(IReadOnlyList<StimulusResumeRecipient> resumes,
        IReadOnlyList<StimulusIngressConsumer> consumers, IReadOnlyList<StimulusIngressLegacyResolution> legacy) =>
        new(Scope, Created, [], resumes, consumers, legacy);

    private static StimulusResumeRecipient Wait(string id, string? correlation = null) => new("workflow", id, "activity-" + id, "node", "resume",
        "event", "hash", Created, null, new(1, Pin, ReceiverScope, null,
            correlation is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [RuntimeMetadataKeys.CorrelationId] = correlation }, Digest));

    private static StimulusIngressSource Source(StimulusIngressCohort cohort, bool broadcast,
        string? correlation = null, StimulusDeliveryScope? origin = null) =>
        new(1, "source", origin ?? ReceiverScope, "client-key",
            new(1, "event", "hash", null, correlation, StimulusRoutingMode.ResumeOnly, "test", new Dictionary<string, string>(),
                null, null, null, StimulusStartKeyScope.Artifact, broadcast, null), Created, cohort);

    private static StimulusIngressConsumer Consumer(StimulusResumeRecipient wait) =>
        new("workflow", "receiver", Pin, ReceiverScope, "protocol", 1,
            [new("slot-" + wait.BookmarkId, wait.BookmarkId, wait.ActivityExecutionId, 1, "visit", "trigger", "activation")]);
}
