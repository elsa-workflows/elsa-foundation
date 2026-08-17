using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublicationProjectionReconcilerTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReplaysPrepareActivateAndRemoveIdempotentlyWithDurablePerKindIntents()
    {
        var fixture = await Fixture.CreateAsync(_now);

        await fixture.Reconciler.PrepareAsync(fixture.Publication);
        await fixture.Reconciler.PrepareAsync(fixture.Publication);
        await fixture.Reconciler.ActivateAsync(fixture.Publication, replacedPublicationId: null);
        await fixture.Reconciler.ActivateAsync(fixture.Publication, replacedPublicationId: null);

        Assert.Equal(1, fixture.Indexer.PrepareCallCount);
        Assert.Single((await fixture.BindingStore.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "orders"))).Items);
        Assert.Single(await fixture.ScheduleStore.ListDueAsync(_now.AddHours(2), 10));

        await fixture.Reconciler.RemoveAsync(fixture.Publication);
        await fixture.Reconciler.RemoveAsync(fixture.Publication);

        var intents = await fixture.IntentStore.ListByPublicationAsync(fixture.Publication.PublicationId);
        // FIVE, not six: preparation is ONE delivery covering both projections, because the indexer chain writes
        // both in one call (FR-B-006 writer census, finding 3). The sixth record used to govern a second,
        // read-back-driven write of the schedule projection — a duplicate whose independent Delivered guard could
        // short-circuit out of step with the write that actually produced the projection.
        Assert.Equal(5, intents.Count);
        Assert.DoesNotContain(intents, intent =>
            intent.ProjectionKind == PublicationProjectionKinds.RecurringSchedules &&
            intent.Operation == PublicationProjectionOperation.Prepare);
        Assert.All(intents, intent => Assert.Equal(PublicationProjectionIntentStatus.Delivered, intent.Status));
        Assert.Empty(await fixture.BindingStore.ListAllByActivationAsync(fixture.Publication.PublicationId));
        Assert.Empty(await fixture.ScheduleStore.ListByActivationAsync(fixture.Publication.PublicationId));
    }

    [Fact]
    public async Task Preparation_writes_each_projection_exactly_once_and_a_replay_cannot_erase_the_schedules()
    {
        // T044/T047. The reconciler used to prepare the recurring projection a SECOND time, from a read-back,
        // under its own delivery record. Because that record was independent of the one governing the indexer
        // call that actually produced the projection, a replay whose bindings record was already Delivered
        // skipped the indexer, then re-prepared the schedules from an empty read-back and erased them.
        var fixture = await Fixture.CreateAsync(_now);

        await fixture.Reconciler.PrepareAsync(fixture.Publication);

        Assert.Equal(1, fixture.Indexer.PrepareCallCount);
        Assert.Equal(1, fixture.ScheduleStore.PrepareActivationCalls);
        Assert.Single(await fixture.ScheduleStore.ListByActivationAsync(fixture.Publication.PublicationId));

        await fixture.Reconciler.PrepareAsync(fixture.Publication);

        // The replay short-circuits BOTH projections together, because one record governs one write per
        // projection. Nothing is re-prepared, and nothing is lost.
        Assert.Equal(1, fixture.Indexer.PrepareCallCount);
        Assert.Equal(1, fixture.ScheduleStore.PrepareActivationCalls);
        Assert.Single(await fixture.ScheduleStore.ListByActivationAsync(fixture.Publication.PublicationId));
    }

    [Fact]
    public async Task FailedPreparationIsPersistedAndRetryConvergesUsingSameIntentIdentity()
    {
        var fixture = await Fixture.CreateAsync(_now, failFirstPreparation: true);
        var intentId = PublicationProjectionReconciler.BuildIntentId(
            fixture.Publication.PublicationId,
            PublicationProjectionKinds.TriggerBindings,
            PublicationProjectionOperation.Prepare);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Reconciler.PrepareAsync(fixture.Publication).AsTask());

        var failed = await fixture.IntentStore.FindAsync(intentId);
        Assert.NotNull(failed);
        Assert.Equal(PublicationProjectionIntentStatus.Failed, failed.Status);
        Assert.Equal(1, failed.AttemptCount);
        Assert.Equal("projection_delivery_failed", failed.LastFailure?.Code);
        Assert.NotNull(failed.NextAttemptAt);

        await fixture.Reconciler.PrepareAsync(fixture.Publication);

        var delivered = await fixture.IntentStore.FindAsync(intentId);
        Assert.Equal(PublicationProjectionIntentStatus.Delivered, delivered!.Status);
        Assert.Equal(2, delivered.AttemptCount);
        Assert.Equal(intentId, PublicationProjectionReconciler.BuildIntentId(
            fixture.Publication.PublicationId,
            PublicationProjectionKinds.TriggerBindings,
            PublicationProjectionOperation.Prepare));
    }

    [Fact]
    public async Task Compensation_NotifiesObserversOnce_AfterTheFinalAuthorityStateIsVisible()
    {
        var fixture = await Fixture.CreateAsync(_now);
        await fixture.Reconciler.PrepareAsync(fixture.Publication);
        var candidateBinding = Assert.Single(await fixture.BindingStore.ListAllByActivationAsync(fixture.Publication.PublicationId));
        var restoredBinding = candidateBinding with
        {
            TriggerBindingId = WorkflowTriggerBinding.BuildId(
                "publication-old",
                candidateBinding.ArtifactId,
                candidateBinding.ExecutableNodeId,
                candidateBinding.StimulusHash),
            ActivationId = "publication-old",
            IsActive = false
        };
        await fixture.BindingStore.PrepareActivationAsync("publication-old", [restoredBinding]);
        var candidateSchedule = Assert.Single(await fixture.ScheduleStore.ListByActivationAsync(fixture.Publication.PublicationId));
        var restoredSchedule = candidateSchedule with
        {
            ScheduleId = RecurringTriggerSchedule.BuildId(
                "publication-old",
                candidateSchedule.ArtifactId,
                "node-root"),
            ActivationId = "publication-old",
            IsActive = false
        };
        await fixture.ScheduleStore.PrepareActivationAsync("publication-old", [restoredSchedule]);
        await fixture.BindingStore.ActivateAsync("publication-old", replacedActivationId: null);
        await fixture.ScheduleStore.ActivateAsync("publication-old", replacedActivationId: null);
        await fixture.Reconciler.ActivateAsync(fixture.Publication, "publication-old");
        fixture.Observer.Clear();

        await fixture.Reconciler.CompensateAsync(fixture.Publication, "publication-old");

        var visible = Assert.Single(fixture.Observer.VisiblePublicationsByNotification);
        Assert.Equal(new[] { "publication-old" }, visible);
        Assert.Empty(await fixture.BindingStore.ListAllByActivationAsync(fixture.Publication.PublicationId));
    }

    [Fact]
    public async Task Restore_ForceReplaysDeliveredPrepareAndActivateAfterProjectionRemoval()
    {
        var fixture = await Fixture.CreateAsync(_now);
        await fixture.Reconciler.PrepareAsync(fixture.Publication);
        await fixture.Reconciler.ActivateAsync(fixture.Publication, replacedPublicationId: null);
        await fixture.Reconciler.RemoveAsync(fixture.Publication);
        Assert.Empty(await fixture.BindingStore.ListAllByActivationAsync(fixture.Publication.PublicationId));

        await fixture.Reconciler.RestoreAsync(fixture.Publication);

        Assert.Equal(2, fixture.Indexer.PrepareCallCount);
        Assert.Single((await fixture.BindingStore.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "orders"))).Items);
        Assert.Single(await fixture.ScheduleStore.ListDueAsync(_now.AddHours(2), 10));
    }

    /// <summary>
    /// Counts preparations of the recurring projection. T047's measuring instrument: exactly one write per
    /// activation, by the indexer chain, with no read-back-driven second write from the reconciler.
    /// </summary>
    private sealed class CountingRecurringScheduleStore : IRecurringTriggerScheduleStore
    {
        private readonly InMemoryRecurringTriggerScheduleStore _inner = new();

        public int PrepareActivationCalls { get; private set; }

        public ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<RecurringTriggerSchedule> schedules, CancellationToken cancellationToken = default)
        {
            PrepareActivationCalls++;
            return _inner.PrepareActivationAsync(activationId, schedules, cancellationToken);
        }

        public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(schedule, cancellationToken);

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByActivationPageAsync(RecurringTriggerScheduleActivationPageQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListByActivationPageAsync(query, cancellationToken);

        public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListByActivationAsync(string activationId, CancellationToken cancellationToken = default) =>
            _inner.ListByActivationAsync(activationId, cancellationToken);

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(RecurringTriggerScheduleArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListByArtifactPageAsync(query, cancellationToken);

        public ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default) =>
            _inner.ActivateAsync(activationId, replacedActivationId, cancellationToken);

        public ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default) =>
            _inner.DeleteByActivationAsync(activationId, cancellationToken);

        public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) =>
            _inner.ListDueAsync(asOf, limit, cancellationToken);

        public ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default) =>
            _inner.FindAsync(scheduleId, cancellationToken);

        public ValueTask<bool> TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken = default) =>
            _inner.TryAdvanceAsync(scheduleId, expectedNextOccurrence, newNextOccurrence, cancellationToken);

        public ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) =>
            _inner.DeleteByArtifactAsync(artifactId, cancellationToken);

        public ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(scheduleId, cancellationToken);
    }

    private sealed class Fixture(
        PublicationRecord publication,
        InMemoryPublicationProjectionIntentStore intentStore,
        InMemoryWorkflowTriggerBindingStore bindingStore,
        CountingRecurringScheduleStore scheduleStore,
        RecordingProjectionIndexer indexer,
        ServingProjectionObserver observer,
        PublicationProjectionReconciler reconciler)
    {
        public PublicationRecord Publication { get; } = publication;
        public InMemoryPublicationProjectionIntentStore IntentStore { get; } = intentStore;
        public InMemoryWorkflowTriggerBindingStore BindingStore { get; } = bindingStore;
        public CountingRecurringScheduleStore ScheduleStore { get; } = scheduleStore;
        public RecordingProjectionIndexer Indexer { get; } = indexer;
        public ServingProjectionObserver Observer { get; } = observer;
        public PublicationProjectionReconciler Reconciler { get; } = reconciler;

        public static async Task<Fixture> CreateAsync(DateTimeOffset now, bool failFirstPreparation = false)
        {
            var executableStore = new InMemoryWorkflowExecutableStore();
            var executable = Executable(now);
            await executableStore.SaveAsync(executable);
            var intentStore = new InMemoryPublicationProjectionIntentStore();
            var bindingStore = new InMemoryWorkflowTriggerBindingStore();
            var scheduleStore = new CountingRecurringScheduleStore();
            var indexer = new RecordingProjectionIndexer(bindingStore, scheduleStore, now, failFirstPreparation);
            var observer = new ServingProjectionObserver(bindingStore);
            var reconciler = new PublicationProjectionReconciler(
                intentStore,
                executableStore,
                indexer,
                bindingStore,
                new FixedTimeProvider(now),
                scheduleStore,
                [observer]);
            var publication = new PublicationRecord(
                PublicationId: "publication-1",
                SlotId: WorkflowActivationSlotIdentity.Create("definition-1", "default"),
                WorkflowDefinitionId: "definition-1",
                WorkflowDefinitionVersionId: "version-1",
                ArtifactId: executable.Identity.ArtifactId,
                SourceReferenceId: "reference-1",
                ExpectedSlotRevision: 0,
                Status: PublicationStatus.Candidate,
                CreatedAt: now,
                ActivatedAt: null,
                RetiredAt: null,
                Failure: null);
            return new Fixture(publication, intentStore, bindingStore, scheduleStore, indexer, observer, reconciler);
        }

        private static WorkflowExecutable Executable(DateTimeOffset now) =>
            new(
                new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
                new ExecutableNode(
                    executableNodeId: "node-root",
                    authoredActivityId: "activity-root",
                    activityType: "Test.Root",
                    activityTypeVersion: "1.0.0",
                    descriptor: new RuntimeActivityDescriptor("Test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
                    inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                    metadata: new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                now,
                new Dictionary<string, string>(),
                IncidentStrategyBuiltIns.FaultReference);
    }

    private sealed class ServingProjectionObserver(IWorkflowTriggerBindingStore bindingStore) : IWorkflowTriggerIndexObserver
    {
        public List<string[]> VisiblePublicationsByNotification { get; } = [];

        public async ValueTask OnTriggersIndexedAsync(
            WorkflowTriggerIndexSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            var visible = await bindingStore.ListAllByStimulusTypeAsync("Event", cancellationToken);
            VisiblePublicationsByNotification.Add(visible
                .Select(x => x.ActivationId!)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray());
        }

        public void Clear() => VisiblePublicationsByNotification.Clear();
    }

    private sealed class RecordingProjectionIndexer(
        IWorkflowTriggerBindingStore bindingStore,
        IRecurringTriggerScheduleStore scheduleStore,
        DateTimeOffset now,
        bool failFirstPreparation) : IWorkflowTriggerIndexer
    {
        private bool _shouldFail = failFirstPreparation;
        public int PrepareCallCount { get; private set; }

        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string publicationId,
            string slotId,
            CancellationToken cancellationToken = default)
        {
            PrepareCallCount++;
            if (_shouldFail)
            {
                _shouldFail = false;
                throw new InvalidOperationException("Projection preparation failed once.");
            }

            var binding = new WorkflowTriggerBinding(
                WorkflowTriggerBinding.BuildId(publicationId, executable.Identity.ArtifactId, "node-root", "orders"),
                executable.Identity.ArtifactId,
                executable.Identity.DefinitionId,
                executable.Identity.ArtifactVersion,
                executable.Identity.ArtifactHash,
                "node-root",
                "Event",
                "orders",
                CorrelationScope: null,
                Metadata: new Dictionary<string, string>(),
                CreatedAt: now,
                ActivationId: publicationId,
                SlotId: slotId,
                IsActive: false);
            var schedule = new RecurringTriggerSchedule(
                RecurringTriggerSchedule.BuildId(publicationId, executable.Identity.ArtifactId, "node-root"),
                executable.Identity.ArtifactId,
                "node-root",
                "Event",
                "orders",
                RecurringScheduleKind.Interval,
                "PT1H",
                now.AddHours(1),
                now,
                ActivationId: publicationId,
                SlotId: slotId,
                IsActive: false);
            await bindingStore.PrepareActivationAsync(publicationId, [binding], cancellationToken);
            await scheduleStore.PrepareActivationAsync(publicationId, [schedule], cancellationToken);
            return [binding];
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
