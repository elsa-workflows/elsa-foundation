using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class SourceOwnedStimulusRouterTests
{
    // The source/cohort/dispatcher are real. The recording history boundary isolates attribution;
    // atomic persistence, append failure and restart remain separate provider contracts.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_start_preserves_only_the_physical_source_cause_in_its_serialized_command(bool captured)
    {
        await using var harness = new Harness();
        await harness.SeedMaterial(); await harness.Bind("binding");
        var cause = captured ? new RuntimeHistoryCausation("publisher-event", "publisher", "source-commit:send") : null;
        var source = await harness.Source(historyCausation: cause);

        await harness.Router.RouteSourceAsync(source);

        var command = Assert.Single(harness.Actors.Accepted).Command;
        var payload = command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal(cause, payload.HistoryCausation);
        Assert.Equal(source.SourceOutboxItemId, payload.StartAuthority!.StimulusDelivery!.SourceOutboxItemId);
        Assert.NotNull((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    [Fact]
    public async Task Saved_start_refuses_causation_not_owned_by_the_physical_source_before_mailbox_admission()
    {
        await using var harness = new Harness();
        await harness.SeedMaterial(); await harness.Bind("binding");
        var cause = new RuntimeHistoryCausation("publisher-event", "publisher", "source-commit:send");
        var source = await harness.Source(historyCausation: cause);
        await harness.Router.RouteSourceAsync(source);
        var snapshot = (await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot!;
        var binding = Assert.Single(snapshot.Starts).Binding;
        var key = KeyedWorkflowStartIdentity.For(source.Intent.IdempotencyKey!, Pin.ArtifactId);
        var request = new WorkflowExecutionStartDispatchRequest(Pin.ArtifactId, "test.saved-event", key.WorkflowExecutionId,
            key.StartKey, null, null, null, source.Intent.Payload, binding.ExecutableNodeId, WorkflowRunKind.PublishedRun,
            null, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy, null, null, snapshot.Scope.TenantId,
            snapshot.Scope.Partition, null, WorkflowExecutableStartAuthority.FromStimulusDelivery(source, binding.TriggerBindingId),
            triggerMetadata: binding.Metadata) { HistoryCausation = cause with { HistoryEventId = "unrelated-event" } };
        var attempts = harness.Actors.Attempts.Count;

        var failure = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Start.DispatchAsync(request));

        Assert.Equal("stimulus-delivery-input-mismatch", failure.ReasonCode);
        Assert.Equal(attempts, harness.Actors.Attempts.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_missing_or_rearmed_wait_records_actual_unmatched_delivery_without_dispatching_it(bool rearmed)
    {
        var recorder = new ObservationRecorder();
        await using var harness = new Harness(observations: recorder);
        await harness.SeedMaterial(); await harness.Wait("existing", "wait");
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, RuntimeHistoryDeliveryFacts.Kinds);
        var owner = (await harness.Executions.FindAsync("existing"))! with { HistoryCapturePolicy = policy };
        await harness.Executions.SaveAsync(owner);
        var cause = new RuntimeHistoryCausation("publisher-event", "publisher", "source-commit:send");
        var source = await harness.Source(historyCausation: cause);
        var observed = (await harness.Bookmarks.ObserveAsync("existing", "wait"))!;
        var expected = StimulusResumeRecipient.From(observed, owner, Scope);
        await harness.Store.InstallAsync(source, new(1, source.SourceOutboxItemId, source.IntentFingerprint,
            StimulusDeliveryScope.From((await harness.Executions.FindAsync("publisher"))!, Scope), Now, "event", "named", [], [expected]));
        await harness.Bookmarks.DeleteAsync("existing", "wait");
        if (rearmed)
        {
            await harness.Wait("existing", "wait");
            await harness.Executions.SaveAsync((await harness.Executions.FindAsync("existing"))! with { HistoryCapturePolicy = policy });
        }

        var result = await harness.Resume.DispatchAsync(source, "existing", "wait");

        Assert.Equal(BookmarkResumeDispatchStatus.NotFound, result.Status);
        Assert.Empty(harness.Actors.Attempts);
        Assert.Collection(recorder.Observations,
            received =>
            {
                Assert.Equal("DeliveryReceived", received.Event.Name);
                Assert.Equal(cause, received.Event.Causation);
            },
            unmatched =>
            {
                Assert.Equal("DeliveryUnmatched", unmatched.Event.Name);
                Assert.Equal(recorder.Observations[0].Event.HistoryEventId, unmatched.Event.Causation!.HistoryEventId);
                Assert.Equal(source.SourceOutboxItemId, unmatched.Event.Causation.DeliveryId);
            });
        Assert.All(recorder.Observations, item =>
        {
            Assert.Equal("existing", item.Event.WorkflowExecutionId);
            Assert.Equal("wait", item.Event.BookmarkId);
            Assert.Equal(expected.ActivityExecutionId, item.Event.ActivityExecutionId);
            Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, item.Event.Authority);
        });
        Assert.DoesNotContain(recorder.Observations, item => item.Event.Name is "BookmarkConsumed" or "ActivityContinuationStarted");
        Assert.Equal(rearmed, await harness.Bookmarks.FindAsync("existing", "wait") is not null);
        Assert.Equal(0, (await harness.Store.FindAsync(source.SourceOutboxItemId))!.DeliveryAttemptCount);
    }

    [Theory]
    [InlineData("eligible", false)]
    [InlineData("eligible", true)]
    [InlineData("removed", false)]
    [InlineData("removed", true)]
    [InlineData("rearmed", false)]
    [InlineData("rearmed", true)]
    [InlineData("terminal-owner", false)]
    [InlineData("terminal-owner", true)]
    public async Task Saved_resume_rechecks_final_eligibility_after_actor_activation_before_enqueue_and_disposition(string condition, bool captured)
    {
        var recorder = captured ? new ObservationRecorder() : null;
        await using var harness = new Harness(observations: recorder);
        await harness.SeedMaterial();
        await harness.Wait("existing", "wait");
        var owner = (await harness.Executions.FindAsync("existing"))!;
        if (captured)
        {
            owner = owner with { HistoryCapturePolicy = new("runtime-history-v3", 3, true, RuntimeHistoryDeliveryFacts.Kinds) };
            await harness.Executions.SaveAsync(owner);
        }
        var cause = captured ? new RuntimeHistoryCausation("publisher-event", "publisher", "source-commit:send") : null;
        var source = await harness.Source(historyCausation: cause);
        var original = (await harness.Bookmarks.ObserveAsync("existing", "wait"))!;
        var expected = StimulusResumeRecipient.From(original, owner, Scope);
        await harness.Store.InstallAsync(source, new(1, source.SourceOutboxItemId, source.IntentFingerprint,
            StimulusDeliveryScope.From((await harness.Executions.FindAsync("publisher"))!, Scope), Now, "event", "named", [], [expected]));
        var activations = 0;
        harness.Actors.BeforeAgent = async activation =>
        {
            activations++;
            Assert.Equal("existing", activation.WorkflowExecutionId);
            Assert.Empty(harness.Actors.Attempts);
            Assert.Equal(original.Generation, (await harness.Bookmarks.ObserveAsync("existing", "wait"))!.Generation);
            Assert.NotNull((await harness.Store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
            if (recorder is not null) Assert.Equal("DeliveryReceived", Assert.Single(recorder.Observations).Event.Name);
            if (condition is "removed" or "rearmed")
                Assert.True(await harness.Bookmarks.DeleteAsync("existing", "wait"));
            if (condition == "rearmed")
            {
                await harness.Wait("existing", "wait");
                await harness.Executions.SaveAsync(owner);
            }
            if (condition == "terminal-owner")
                await harness.Executions.SaveAsync(owner with { Status = WorkflowExecutionStatus.Completed, CompletedAt = Now });
        };

        var result = await harness.Resume.DispatchAsync(source, "existing", "wait");

        Assert.Equal(1, activations);
        var eligible = condition == "eligible";
        Assert.Equal(eligible ? BookmarkResumeDispatchStatus.Dispatched : BookmarkResumeDispatchStatus.NotFound, result.Status);
        if (eligible)
        {
            var queued = Assert.Single(harness.Actors.Attempts);
            Assert.Equal(queued, Assert.Single(harness.Actors.Accepted));
            var payload = queued.Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
            Assert.Equal("wait", payload.BookmarkId);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected), JsonSerializer.SerializeToElement(payload.ExpectedBookmarkResume)));
        }
        else
        {
            Assert.Empty(harness.Actors.Attempts);
            Assert.Empty(harness.Actors.Accepted);
            Assert.Null(result.CommandDispatch);
        }
        if (condition == "removed") Assert.Null(await harness.Bookmarks.FindAsync("existing", "wait"));
        else
        {
            var retained = (await harness.Bookmarks.ObserveAsync("existing", "wait"))!;
            if (condition == "rearmed") Assert.NotEqual(original.Generation, retained.Generation);
            else Assert.Equal(original.Generation, retained.Generation);
        }
        if (recorder is not null)
        {
            Assert.Collection(recorder.Observations,
                received => { Assert.Equal("DeliveryReceived", received.Event.Name); Assert.Equal(cause, received.Event.Causation); },
                disposition =>
                {
                    Assert.Equal(eligible ? "DeliveryDispatched" : "DeliveryUnmatched", disposition.Event.Name);
                    Assert.Equal(recorder.Observations[0].Event.HistoryEventId, disposition.Event.Causation!.HistoryEventId);
                    Assert.Equal(source.SourceOutboxItemId, disposition.Event.Causation.DeliveryId);
                });
            Assert.All(recorder.Observations, observation =>
            {
                Assert.Equal("existing", observation.Event.WorkflowExecutionId);
                Assert.Equal("wait", observation.Event.BookmarkId);
                Assert.Equal(expected.ActivityExecutionId, observation.Event.ActivityExecutionId);
                Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, observation.Event.Authority);
            });
            Assert.DoesNotContain(recorder.Observations, observation => observation.Event.Name is "BookmarkConsumed" or "ActivityContinuationStarted");
        }
        Assert.Equal(0, (await harness.Store.FindAsync(source.SourceOutboxItemId))!.DeliveryAttemptCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_dispatch_exception_preserves_exact_target_attribution_without_asserting_business_failure(bool acceptedThenAckLost)
    {
        var recorder = new ObservationRecorder();
        await using var harness = new Harness(observations: recorder);
        await harness.SeedMaterial();
        await harness.Wait("existing", "wait");
        var owner = (await harness.Executions.FindAsync("existing"))! with
            { HistoryCapturePolicy = new("runtime-history-v3", 3, true, RuntimeHistoryDeliveryFacts.Kinds) };
        await harness.Executions.SaveAsync(owner);
        var cause = new RuntimeHistoryCausation("publisher-event", "publisher", "source-commit:send");
        var source = await harness.Source(historyCausation: cause);
        var original = (await harness.Bookmarks.ObserveAsync("existing", "wait"))!;
        var expected = StimulusResumeRecipient.From(original, owner, Scope);
        var winner = await harness.Store.InstallAsync(source, new(1, source.SourceOutboxItemId, source.IntentFingerprint,
            StimulusDeliveryScope.From((await harness.Executions.FindAsync("publisher"))!, Scope), Now, "event", "named", [], [expected]));
        var activationFailure = new IOException("Constructed actor activation outage.");
        if (acceptedThenAckLost) harness.Actors.LoseNextAck = true;
        else harness.Actors.BeforeAgent = _ => Task.FromException(activationFailure);

        var failure = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Resume.DispatchAsync(source, "existing", "wait").AsTask());

        Assert.True(failure.IsUnavailable);
        if (acceptedThenAckLost)
        {
            Assert.IsType<IOException>(failure.InnerException);
            Assert.Equal("Constructed acknowledgment loss after recorded mailbox acceptance.", failure.InnerException!.Message);
            Assert.Equal(Assert.Single(harness.Actors.Attempts), Assert.Single(harness.Actors.Accepted));
        }
        else
        {
            Assert.Same(activationFailure, failure.InnerException);
            Assert.Empty(harness.Actors.Attempts);
            Assert.Empty(harness.Actors.Accepted);
        }
        Assert.Collection(recorder.Observations,
            received =>
            {
                Assert.Equal("DeliveryReceived", received.Event.Name);
                Assert.Equal(cause, received.Event.Causation);
            },
            faulted =>
            {
                Assert.Equal("DeliveryFaulted", faulted.Event.Name);
                Assert.Equal("dispatch-threw", faulted.Event.Metadata["history.reasonCode"]);
                Assert.Equal(recorder.Observations[0].Event.HistoryEventId, faulted.Event.Causation!.HistoryEventId);
                Assert.Equal("existing", faulted.Event.Causation.WorkflowExecutionId);
                Assert.Equal(source.SourceOutboxItemId, faulted.Event.Causation.DeliveryId);
            });
        // This proves emitted attribution at the source boundary. The recording adapter is not a
        // persistence/query acceptance fixture, and an acknowledgement loss says nothing about callback effects.
        Assert.All(recorder.Observations, observation =>
        {
            Assert.Equal("existing", observation.Event.WorkflowExecutionId);
            Assert.Equal(expected.BookmarkId, observation.Event.BookmarkId);
            Assert.Equal(expected.ActivityExecutionId, observation.Event.ActivityExecutionId);
            Assert.Equal(expected.ExecutableNodeId, observation.Event.ExecutableNodeId);
            Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, observation.Event.Authority);
            Assert.Equal(RuntimeHistoryEventCategory.DeliveryObservation, observation.Event.Category);
        });
        Assert.DoesNotContain(recorder.Observations, observation => observation.Event.Name is "ActivityFaulted" or "BookmarkConsumed" or "ActivityContinuationStarted");
        Assert.Equal(original.Generation, (await harness.Bookmarks.ObserveAsync("existing", "wait"))!.Generation);
        var retainedSource = (await harness.Store.FindAsync(source.SourceOutboxItemId))!;
        Assert.Equal(0, retainedSource.DeliveryAttemptCount);
        Assert.True(winner.IsEquivalentTo(retainedSource.RecipientSnapshot));
    }

    private sealed class ObservationRecorder : IRuntimeHistoryObservationWriter, IRuntimeHistoryStore
    {
        public List<RuntimeHistoryObservation> Observations { get; } = [];
        public Func<RuntimeHistoryObservation, Exception?>? Failure { get; set; }
        public bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => ReferenceEquals(selectedStore, this);
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
        {
            if (Failure?.Invoke(observation) is { } failure)
                return ValueTask.FromException<RuntimeHistoryObservationResult>(failure);
            Observations.Add(observation);
            return ValueTask.FromResult(new RuntimeHistoryObservationResult(true, observation.Event));
        }
        public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
