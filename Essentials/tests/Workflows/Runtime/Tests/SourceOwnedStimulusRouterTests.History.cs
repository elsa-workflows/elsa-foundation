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

    private sealed class ObservationRecorder : IRuntimeHistoryObservationWriter, IRuntimeHistoryStore
    {
        public List<RuntimeHistoryObservation> Observations { get; } = [];
        public bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => ReferenceEquals(selectedStore, this);
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
        {
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
