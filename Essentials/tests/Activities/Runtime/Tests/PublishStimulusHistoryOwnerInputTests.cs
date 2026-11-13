using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>Actual feature/enricher and native input models with a controlled source port; no capture/writer/handler execution.</summary>
public sealed class PublishStimulusHistoryOwnerInputTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true, ["StimulusPublicationCommitted", "StimulusRoutingAttemptObserved", "StimulusRoutingDispositionObserved"]);
    private static readonly RuntimeHistoryCausation Incoming = new("receipt", "publisher", "incoming", "command", "work");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_feature_producer_stamps_original_event_identity_and_safe_facts_before_capture(bool pendingSupplied)
    {
        using var f = new Fixture(Policy);
        var original = Commit(pendingSupplied);
        var enriched = await f.EnrichAsync(original);
        var intent = Assert.Single(enriched.PostCommitIntents);
        var origin = intent.HistoryCausation!;
        Assert.Equal(RuntimeHistoryIdentity.CreateId("checkpoint", "StimulusPublicationCommitted", "send"), origin.HistoryEventId);
        Assert.Equal("publisher", origin.WorkflowExecutionId);
        Assert.Equal("commit:send", origin.DeliveryId);
        Assert.Equal("command", origin.CommandId);
        Assert.Equal("work", origin.SchedulerWorkItemId);
        var fact = Assert.Single(enriched.HistorySourceFacts!);
        Assert.Equal(RuntimeHistorySourceFactKind.StimulusPublicationCommitted, fact.Kind);
        Assert.Equal("send", fact.SubjectId);
        Assert.Equal(At, fact.OccurredAt);
        Assert.Equal("activity", fact.ActivityExecutionId);
        Assert.Equal(Incoming, fact.Causation); // Incoming cause belongs to the event, outgoing origin points to that event.
        Assert.DoesNotContain("private", JsonSerializer.Serialize(fact));
        Assert.Null(Assert.Single(original.PostCommitIntents).HistoryCausation);
        Assert.Null(original.HistorySourceFacts);
        if (pendingSupplied)
        {
            var pending = Assert.Single(enriched.StateChanges.PostCommitOutbox).State;
            Assert.Equal(origin, pending.Intent.HistoryCausation);
            Assert.Equal(Scope, pending.StimulusDeliverySourceScope);
            Assert.Null(pending.StimulusDeliveryProtocolVersion); // Origin stamping cannot invent a routing protocol/cohort.
            Assert.Null(pending.RecipientSnapshot);
            RuntimeCheckpointCommitValidator.Validate(enriched);
        }
        else Assert.Empty(enriched.StateChanges.PostCommitOutbox);
        var again = await f.EnrichAsync(enriched);
        Assert.Equal(origin, Assert.Single(again.PostCommitIntents).HistoryCausation);
        Assert.Single(again.HistorySourceFacts!);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(enriched), RuntimeCheckpointCommitFingerprint.Compute(again));
    }

    [Theory]
    [InlineData("absent-source")]
    [InlineData("legacy")]
    [InlineData("older")]
    [InlineData("core-only")]
    [InlineData("source-only")]
    [InlineData("missing-attempt")]
    [InlineData("missing-disposition")]
    public async Task Original_source_absence_and_older_pinned_vocabularies_preserve_native_input_identity(string profile)
    {
        var policy = profile switch
        {
            "older" => new RuntimeHistoryCapturePolicy("runtime-history-v2", 2, true, ["StimulusPublicationCommitted"]),
            "source-only" => new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, ["StimulusPublicationCommitted"]),
            "missing-attempt" => new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, ["StimulusPublicationCommitted", "StimulusRoutingDispositionObserved"]),
            "missing-disposition" => new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, ["StimulusPublicationCommitted", "StimulusRoutingAttemptObserved"]),
            "core-only" => new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, ["ActivityCompleted"]),
            _ => null
        };
        using var f = new Fixture(policy, sourceInstalled: profile != "absent-source");
        var original = Commit(true);
        var enriched = await f.EnrichAsync(original);
        Assert.Same(original, enriched);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(original), RuntimeCheckpointCommitFingerprint.Compute(enriched));
        Assert.Null(Assert.Single(enriched.PostCommitIntents).HistoryCausation);
        Assert.Null(enriched.HistorySourceFacts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Synthetic_flush_and_previously_captured_inputs_preserve_original_source_without_reentry(bool captured)
    {
        using var f = new Fixture(Policy);
        var original = await f.EnrichAsync(Commit(true));
        var retained = captured
            ? original with { HistoryBatches = [new(Policy, "checkpoint", At, false, [])] }
            : original with
            {
                CommitId = "flush-commit",
                Checkpoint = new("flush-checkpoint", "CoalescedSegmentFlush", "publisher", At.AddMinutes(1), [],
                    new Dictionary<string, string> { [RuntimeMetadataKeys.CoalescedFlush] = "true" })
            };
        var calls = f.Source.Calls;
        Assert.Same(retained, await f.EnrichAsync(retained));
        Assert.Equal(calls, f.Source.Calls);
        Assert.Equal(Assert.Single(original.PostCommitIntents).HistoryCausation, Assert.Single(retained.PostCommitIntents).HistoryCausation);
        Assert.Equal(Assert.Single(original.StateChanges.PostCommitOutbox).State, Assert.Single(retained.StateChanges.PostCommitOutbox).State);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(retained), RuntimeCheckpointCommitFingerprint.Compute(await f.EnrichAsync(retained)));
    }

    [Fact]
    public async Task Unrelated_native_work_does_not_call_the_source_port()
    {
        using var f = new Fixture(Policy);
        var unrelated = Commit(false) with { PostCommitIntents = [new("other", "publisher", "other.kind", At, null, null, null)] };
        Assert.Same(unrelated, await f.EnrichAsync(unrelated));
        Assert.Equal(0, f.Source.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scope_drift_before_or_after_source_factory_refuses_without_mutating_original_inputs(bool after)
    {
        using var f = new Fixture(Policy);
        if (after) f.Source.After = () => f.Access.Current = PersistenceAccessContext.Scoped(new("foreign"));
        else f.Source.Before = () => f.Access.Current = PersistenceAccessContext.Scoped(new("foreign"));
        var original = Commit(true);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.EnrichAsync(original));
        Assert.Null(Assert.Single(original.PostCommitIntents).HistoryCausation);
        Assert.Null(Assert.Single(original.StateChanges.PostCommitOutbox).State.Intent.HistoryCausation);
    }

    [Fact]
    public void Stamping_preserves_wait_delay_metadata_payload_and_in_process_conduit()
    {
        var conduit = new RuntimeSchedulerWorkItem("carrier", "publisher", "command", WorkflowExecutionCommandKind.Checkpoint,
            "envelope", "key", At, At);
        var intent = new RuntimePostCommitIntent("send", "publisher", PublishStimulusConstants.PublishStimulusIntentKind, At, "activity", "key",
            JsonSerializer.SerializeToElement(new { value = "private" }), new Dictionary<string, string> { ["private"] = "metadata" },
            "wait", RuntimeWaitDependentIntentFailurePolicy.CancelWait, conduit, At.AddMinutes(1));
        var original = Envelope(intent);
        var stamped = RuntimeHistorySourceFacts.WithPostCommitCausation(original, Origin, Scope);
        var actual = Assert.Single(stamped.PostCommitIntents);
        Assert.Equal(intent.Payload!.Value.GetRawText(), actual.Payload!.Value.GetRawText());
        Assert.Single(actual.Metadata);
        Assert.Equal("metadata", actual.Metadata["private"]);
        Assert.Equal(intent.DependsOnWaitRegistrationId, actual.DependsOnWaitRegistrationId);
        Assert.Equal(intent.WaitFailurePolicy, actual.WaitFailurePolicy);
        Assert.Equal(intent.NotBefore, actual.NotBefore);
        Assert.Same(conduit, actual.MaterializedSchedulerWorkItem);
        Assert.Same(stamped, RuntimeHistorySourceFacts.WithPostCommitCausation(stamped, Origin, Scope));
    }

    [Theory]
    [InlineData("event")]
    [InlineData("run")]
    [InlineData("delivery")]
    [InlineData("blank-command")]
    [InlineData("oversized")]
    public void Owner_origins_must_be_bounded_and_match_the_exact_original_source(string problem)
    {
        var original = Commit(false);
        var cause = Origin(Assert.Single(original.PostCommitIntents))!;
        cause = problem switch
        {
            "event" => cause with { HistoryEventId = null }, "run" => cause with { WorkflowExecutionId = "foreign" },
            "delivery" => cause with { DeliveryId = "another:source" }, "blank-command" => cause with { CommandId = " " },
            _ => cause with { SchedulerWorkItemId = new string('x', 1025) }
        };
        Assert.Throws<InvalidOperationException>(() => RuntimeHistorySourceFacts.WithPostCommitCausation(original, _ => cause, Scope));
        Assert.Null(Assert.Single(original.PostCommitIntents).HistoryCausation);
    }

    [Theory]
    [InlineData("delivering")]
    [InlineData("delivered")]
    [InlineData("redriven")]
    [InlineData("foreign-scope")]
    [InlineData("foreign-source")]
    public void Already_delivering_or_foreign_pending_inputs_cannot_be_retroactively_stamped(string state)
    {
        var original = Commit(true);
        var item = Assert.Single(original.StateChanges.PostCommitOutbox).State;
        if (state is "foreign-scope" or "foreign-source")
            item = new(state == "foreign-source" ? "foreign:send" : item.OutboxItemId, item.Intent, item.Status, item.RecordedAt, item.AvailableAt,
                item.RetryPolicy, 0, null, null, null, null, item.Metadata, 0, null, null, state == "foreign-scope" ? new("foreign") : null, null);
        else
        {
            var claim = RuntimePostCommitOutboxClaimTransitions.Claim(item, new("owner", At, TimeSpan.FromMinutes(1), 1));
            item = state == "delivering" ? claim.Item : RuntimePostCommitOutboxClaimTransitions.Complete(claim.Item, claim,
                new(item.OutboxItemId, state == "delivered" ? RuntimePostCommitOutboxStatus.Delivered : RuntimePostCommitOutboxStatus.FailedFinal, At.AddSeconds(1)));
            if (state == "redriven") item = RuntimePostCommitOutboxClaimTransitions.RedriveFailedFinal(item, At.AddSeconds(2));
        }
        var changed = original with { StateChanges = original.StateChanges.WithPostCommitOutbox([
            new(item.OutboxItemId, RuntimeStateChangeOperation.Upsert, item, original.Metadata)]) };
        Assert.Throws<InvalidOperationException>(() => RuntimeHistorySourceFacts.WithPostCommitCausation(changed, Origin, Scope));
        Assert.Null(item.Intent.HistoryCausation);
    }

    [Fact]
    public async Task A_conflicting_original_origin_is_not_rebound_by_the_actual_producer()
    {
        using var f = new Fixture(Policy);
        var old = new RuntimePostCommitIntent("send", "publisher", PublishStimulusConstants.PublishStimulusIntentKind, At, "activity", "key", null)
            { HistoryCausation = new("another-event", "publisher", "commit:send") };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.EnrichAsync(Envelope(old)));
        Assert.Equal("another-event", old.HistoryCausation.HistoryEventId);
    }

    private static RuntimeHistoryCausation? Origin(RuntimePostCommitIntent intent) => new("event", "publisher", "commit:" + intent.IntentId);
    private static RuntimeCheckpointCommit Envelope(RuntimePostCommitIntent intent) => new("commit",
        new("checkpoint", "name", "publisher", At, ["activity"], new Dictionary<string, string> { [RuntimeMetadataKeys.CommandId] = "command", [RuntimeMetadataKeys.SchedulerWorkItemId] = "work" })
            { HistoryCausation = Incoming }, new(null, null, [], [], [], [], []), [intent], new Dictionary<string, string>());
    private static RuntimeCheckpointCommit Commit(bool pending)
    {
        var original = Envelope(new("send", "publisher", PublishStimulusConstants.PublishStimulusIntentKind, At, "activity", "key",
            JsonSerializer.SerializeToElement(new { value = "private input" })));
        return pending ? original with { StateChanges = original.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(original)) } : original;
    }
    private sealed class MutableAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(Scope); }
    private sealed class RecordingSource(RuntimeHistoryCapturePolicy? policy) : IRuntimeHistorySourceExtension
    {
        internal int Calls; internal Action? Before, After;
        public ValueTask<RuntimeCheckpointCommit> AddFactsAsync(RuntimeCheckpointCommit commit,
            Func<RuntimeHistoryCapturePolicy, IEnumerable<RuntimeHistorySourceFact>> factory, CancellationToken cancellationToken = default)
        {
            Calls++; Before?.Invoke();
            if (!RuntimeHistorySourceFacts.IsFinalProfile(policy)) return ValueTask.FromResult(commit);
            var facts = factory(policy!).ToArray(); After?.Invoke();
            if (facts.Length == 0) return ValueTask.FromResult(commit);
            // Controlled input port; only dedupes repeated identical owner inputs, never creates a batch or native event.
            return ValueTask.FromResult(commit with { HistorySourceFacts = (commit.HistorySourceFacts ?? []).Concat(facts)
                .DistinctBy(fact => (fact.Kind, fact.SubjectId)).ToArray() });
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider provider; private readonly IServiceScope scope;
        internal MutableAccess Access { get; } = new();
        internal RecordingSource Source { get; }
        internal Fixture(RuntimeHistoryCapturePolicy? policy, bool sourceInstalled = true)
        {
            Source = new(policy); var services = new ServiceCollection();
            services.AddSingleton<IPersistenceAccessContextAccessor>(Access);
            if (sourceInstalled) services.AddSingleton<IRuntimeHistorySourceExtension>(Source);
            new ActivitiesPrimitivesFeature().ConfigureServices(services);
            provider = services.BuildServiceProvider(validateScopes: true); scope = provider.CreateScope();
        }
        internal async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit)
        {
            foreach (var enricher in scope.ServiceProvider.GetServices<IRuntimeCheckpointCommitEnricher>().OrderBy(enricher => enricher.Order))
                commit = await enricher.EnrichAsync(commit);
            return commit;
        }
        public void Dispose() { scope.Dispose(); provider.Dispose(); }
    }
}
