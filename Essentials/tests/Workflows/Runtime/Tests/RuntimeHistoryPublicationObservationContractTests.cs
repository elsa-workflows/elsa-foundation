using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Closed provider-neutral observation models with supplied source/claim; not physical delivery authority.</summary>
public sealed class RuntimeHistoryPublicationObservationContractTests
{
    private const string Attempt = "StimulusRoutingAttemptObserved", Disposition = "StimulusRoutingDispositionObserved";
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true, ["StimulusPublicationCommitted", Attempt, Disposition]);
    private static readonly PersistenceScope Scope = new("scope");
    private static readonly RuntimeHistoryCausation Origin = new("publication-event", "run", "commit:send", "command", "work");

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Source_observations_roundtrip_exact_safe_common_fields_and_link_the_disposition_to_its_invocation(bool web)
    {
        var source = Source(); var options = web ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions();
        var attempt = Build(source);
        var disposition = RuntimeHistorySourceFacts.PublicationObservation(Policy, source, "receipt", Disposition, At.AddSeconds(1), "saved-cohort-routing-acknowledged-observed");
        var restored = JsonSerializer.Deserialize<RuntimeHistoryObservation>(JsonSerializer.Serialize(disposition, options), options)!;
        Assert.Equal(RuntimeHistoryIdentity.Compute(disposition.Event), RuntimeHistoryIdentity.Compute(restored.Event));
        Assert.Equal(Origin, attempt.Event.Causation);
        Assert.Equal(attempt.Event.HistoryEventId, disposition.Event.Causation!.HistoryEventId);
        Assert.Equal("commit:send", disposition.Event.Causation.DeliveryId);
        Assert.Equal("outbox-attempt:1:fence:1", restored.Event.AttemptId);
        Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, restored.Event.Authority);
        Assert.Null(restored.Event.CheckpointId); Assert.Null(restored.Event.CheckpointOrdinal); Assert.Null(restored.Event.PhysicalCommitId);
        Assert.Null(restored.Event.Payload); Assert.Equal(0, restored.Event.Sequence);
        RuntimeHistorySafety.ValidateObservation(restored);
    }

    [Fact]
    public void Receipt_identity_is_stable_for_an_immutable_retry_and_distinct_for_another_invocation()
    {
        var original = Build(Source()); var same = Build(Source());
        Assert.Equal(original.Event.HistoryEventId, same.Event.HistoryEventId);
        Assert.Equal(RuntimeHistoryIdentity.Compute(original.Event), RuntimeHistoryIdentity.Compute(same.Event));
        var another = RuntimeHistorySourceFacts.PublicationObservation(Policy, Source(), "another", Attempt, At, "handler-invocation-observed");
        Assert.NotEqual(original.Event.HistoryEventId, another.Event.HistoryEventId);
        Assert.Equal(original.Event.Causation, another.Event.Causation);
        Assert.Single(original.Event.Metadata); Assert.Null(original.Event.ExecutableNodeId);
    }

    [Theory]
    [InlineData("event")] [InlineData("run")] [InlineData("delivery")] [InlineData("scope")]
    [InlineData("absent-origin")] [InlineData("opaque-bound")]
    public void Supplied_origins_must_name_the_exact_bounded_source(string problem)
    {
        var cause = problem switch
        {
            "event" => Origin with { HistoryEventId = null }, "run" => Origin with { WorkflowExecutionId = "foreign" },
            "delivery" => Origin with { DeliveryId = "foreign:send" }, "opaque-bound" => Origin with { CommandId = new string('x', 1025) }, _ => Origin
        };
        var source = Source(cause, problem == "scope" ? new("foreign") : Scope, problem != "absent-origin");
        Assert.Throws<InvalidOperationException>(() => Build(source));
    }

    [Theory]
    [InlineData("source-only")] [InlineData("missing-source")] [InlineData("missing-attempt")]
    [InlineData("missing-disposition")] [InlineData("older")]
    public void A_later_observer_does_not_reinterpret_an_incomplete_or_older_pinned_vocabulary(string missing)
    {
        var kinds = missing switch
        {
            "source-only" => new[] { "StimulusPublicationCommitted" }, "missing-source" => [Attempt, Disposition],
            "missing-attempt" => ["StimulusPublicationCommitted", Disposition],
            "missing-disposition" => ["StimulusPublicationCommitted", Attempt], _ => Policy.SupportedKinds.ToArray()
        };
        var policy = new RuntimeHistoryCapturePolicy(missing == "older" ? "runtime-history-v2" : "runtime-history-v3", missing == "older" ? 2 : 3, true, kinds);
        Assert.Throws<InvalidOperationException>(() => RuntimeHistorySourceFacts.PublicationObservation(policy, Source(), "receipt", Attempt, At, "handler-invocation-observed"));
    }

    [Theory]
    [InlineData("DeliveryReceived")] [InlineData("DeliveryFaulted")]
    public void Admitted_older_delivery_names_cannot_bypass_the_publication_factory_contract(string name)
    {
        var olderVocabulary = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, [name]);
        Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeHistorySourceFacts.PublicationObservation(olderVocabulary, Source(), "receipt",
            name, At, "private exception text"));
    }

    [Theory]
    [InlineData("private-reason")] [InlineData("extra-metadata")] [InlineData("missing-attempt")]
    [InlineData("bookmark")] [InlineData("foreign-run")] [InlineData("missing-cause")]
    public void The_shared_writer_validator_rejects_unsafe_owner_shapes_even_when_the_factory_is_bypassed(string problem)
    {
        var metadata = new Dictionary<string, string> { ["history.reasonCode"] = "handler-invocation-observed" };
        if (problem == "private-reason") metadata["history.reasonCode"] = "private exception text";
        if (problem == "extra-metadata") metadata["history.status"] = "not-an-owner-observation-field";
        var e = new RuntimeHistoryEvent("observation", "run", 0, RuntimeHistoryEventCategory.DeliveryObservation, Attempt, At, metadata: metadata)
        {
            Authority = RuntimeHistoryAuthority.DeliveryObservation,
            AttemptId = problem == "missing-attempt" ? null : "attempt",
            BookmarkId = problem == "bookmark" ? "wait" : null,
            Causation = problem == "missing-cause" ? null : problem == "foreign-run" ? Origin with { WorkflowExecutionId = "foreign" } : Origin
        };
        Assert.Throws<InvalidOperationException>(() => RuntimeHistorySafety.ValidateObservation(new(Policy, e)));
    }

    private static RuntimeHistoryObservation Build(PostCommitDeliveryContext source) =>
        RuntimeHistorySourceFacts.PublicationObservation(Policy, source, "receipt", Attempt, At, "handler-invocation-observed");
    private static PostCommitDeliveryContext Source(RuntimeHistoryCausation? cause = null, PersistenceScope? pendingScope = null, bool stamped = true)
    {
        var intent = new RuntimePostCommitIntent("send", "run", "test.publication", At, "activity", "key", null)
            { HistoryCausation = stamped ? cause ?? Origin : null };
        var item = new RuntimePostCommitOutboxItem("commit:send", intent, RuntimePostCommitOutboxStatus.Pending, At, At,
            null, 0, null, null, null, null, null, 0, null, null, pendingScope ?? Scope, null);
        return new(RuntimePostCommitOutboxClaimTransitions.Claim(item, new("owner", At, TimeSpan.FromMinutes(1), 1)), Scope);
    }
}
