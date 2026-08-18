using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// RED contract for ADR 0043's prepared-first, revisioned publication activation boundary.
/// </summary>
/// <remarks>
/// Rewired for FR-B-006: publishing's activator is now a caller of <see cref="WorkflowActivationCoordinator"/>,
/// so the injection points moved from <c>IPublicationProjectionPreparer</c> to the coordinator's own
/// collaborators. Every assertion below is the one this file made before the retarget — none was relaxed.
/// </remarks>
public sealed class PublicationActivationTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowActivationAuthority _slotStore = new();
    private readonly InMemoryPublicationRecordStore _publicationStore = new();
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _referenceStore = new();

    [Fact]
    public async Task ConcurrentCandidatesFromTheSameObservedRevisionHaveExactlyOneWinner()
    {
        await SeedActivePublicationAsync("publication-current");
        var projectionPreparer = new BarrierTriggerIndexer(participantCount: 2);
        var activator = NewActivator(projectionPreparer);
        var firstCandidate = Candidate("publication-first", expectedSlotRevision: 1);
        var secondCandidate = Candidate("publication-second", expectedSlotRevision: 1);

        var results = await Task.WhenAll(
            activator.ActivateAsync(await RequestAsync(firstCandidate)).AsTask(),
            activator.ActivateAsync(await RequestAsync(secondCandidate)).AsTask());

        var winner = Assert.Single(results.Where(result => result.Succeeded));
        var loser = Assert.Single(results.Where(result => !result.Succeeded));
        var currentSlot = await _slotStore.FindAsync("definition-1", "default");
        var oldPublication = await _publicationStore.FindAsync("publication-current");
        var losingPublication = await _publicationStore.FindAsync(
            currentSlot!.ActiveActivationId == firstCandidate.PublicationId
                ? secondCandidate.PublicationId
                : firstCandidate.PublicationId);

        Assert.Equal(2, currentSlot.Revision);
        Assert.Contains(currentSlot.ActiveActivationId, new[] { firstCandidate.PublicationId, secondCandidate.PublicationId });
        Assert.Equal(currentSlot.ActiveActivationId, winner.Publication.PublicationId);
        Assert.Equal("slot_revision_conflict", loser.Failure?.Code);
        Assert.Equal(PublicationStatus.Retired, oldPublication!.Status);
        Assert.Equal(PublicationStatus.Failed, losingPublication!.Status);
    }

    [Fact]
    public async Task ProjectionPreparationFailureLeavesPriorAuthorityAndServingStateUntouched()
    {
        await SeedActivePublicationAsync("publication-current");
        var activator = NewActivator(new ThrowingTriggerIndexer());
        var candidate = Candidate("publication-failing", expectedSlotRevision: 1);

        var result = await activator.ActivateAsync(await RequestAsync(candidate));

        var currentSlot = await _slotStore.FindAsync("definition-1", "default");
        var oldPublication = await _publicationStore.FindAsync("publication-current");
        var failedCandidate = await _publicationStore.FindAsync(candidate.PublicationId);

        Assert.False(result.Succeeded);
        Assert.Equal("projection_preparation_failed", result.Failure?.Code);
        Assert.Equal("publication-current", currentSlot!.ActiveActivationId);
        Assert.Equal(1, currentSlot.Revision);
        Assert.Equal(PublicationStatus.Active, oldPublication!.Status);
        Assert.Null(oldPublication.RetiredAt);
        Assert.Equal(PublicationStatus.Failed, failedCandidate!.Status);
        Assert.NotNull(failedCandidate.Failure);
    }

    /// <summary>
    /// A journal write that fails AFTER the slot has flipped must not un-activate the candidate — approved
    /// behaviour change, Joey 2026-08-16.
    /// </summary>
    /// <remarks>
    /// FR-B-006 makes the activation slot the sole authority on serving: "Status … MUST NOT be consulted to
    /// decide serving; divergence resolves in favour of the slot." Here the coordinator's sequence completed —
    /// the slot flipped, both projections serve — and only the <see cref="PublicationRecord"/> transition to
    /// <c>Active</c> failed. Rolling back at that point would make journal availability a dependency of serving,
    /// which is exactly the coupling FR-B-006 forbids. So the activation stands, publishing reports the success
    /// that actually happened, and the journal/slot divergence is logged as an operational incident instead.
    /// The assertions below were inverted from "prior authority restored" for that reason, not to make a test
    /// pass; every other assertion in this file is the one it made before the retarget.
    /// </remarks>
    [Fact]
    public async Task CandidateActivationStateJournalFailureLeavesTheFlippedSlotServing()
    {
        await SeedActivePublicationAsync("publication-current");
        var candidate = Candidate("publication-failing", expectedSlotRevision: 1);
        var projectionPreparer = new TrackingTriggerBindingStore("publication-current");
        var failingStore = new FailOncePublicationRecordStore(
            _publicationStore,
            (publication, expectedStatus) =>
                publication.PublicationId == candidate.PublicationId &&
                publication.Status == PublicationStatus.Active &&
                expectedStatus == PublicationStatus.Candidate);
        var activator = NewActivator(new NoopTriggerIndexer(), failingStore, projectionPreparer);

        var result = await activator.ActivateAsync(await RequestAsync(candidate));

        var currentSlot = await _slotStore.FindAsync("definition-1", "default");
        var oldPublication = await _publicationStore.FindAsync("publication-current");
        var storedCandidate = await _publicationStore.FindAsync(candidate.PublicationId);
        // Activation succeeded, so publishing reports success — the candidate is what a stimulus will now start.
        Assert.True(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.Equal(PublicationStatus.Active, result.Publication.Status);
        // The slot and both serving projections stay where the coordinator left them.
        Assert.Equal("publication-failing", currentSlot!.ActiveActivationId);
        Assert.Equal("publication-failing", projectionPreparer.CurrentPublicationId);
        // The journal is the part that did not converge: the candidate's row never left Candidate, and the
        // predecessor's retirement never ran because the transition before it threw.
        Assert.Equal(PublicationStatus.Candidate, storedCandidate!.Status);
        Assert.Equal(PublicationStatus.Active, oldPublication!.Status);
    }

    /// <summary>
    /// Same approved FR-B-006 behaviour as above, one step later: the candidate's own journal row transitioned,
    /// and the failure is in retiring the record of the publication it replaced. Serving is unaffected either
    /// way — see the remarks on <see cref="CandidateActivationStateJournalFailureLeavesTheFlippedSlotServing"/>.
    /// </summary>
    [Fact]
    public async Task ReplacedPublicationRetirementJournalFailureLeavesTheFlippedSlotServing()
    {
        await SeedActivePublicationAsync("publication-current");
        var candidate = Candidate("publication-failing", expectedSlotRevision: 1);
        var projectionPreparer = new TrackingTriggerBindingStore("publication-current");
        var failingStore = new FailOncePublicationRecordStore(
            _publicationStore,
            (publication, expectedStatus) =>
                publication.PublicationId == "publication-current" &&
                publication.Status == PublicationStatus.Retired &&
                expectedStatus == PublicationStatus.Active);
        var activator = NewActivator(new NoopTriggerIndexer(), failingStore, projectionPreparer);

        var result = await activator.ActivateAsync(await RequestAsync(candidate));

        var currentSlot = await _slotStore.FindAsync("definition-1", "default");
        var oldPublication = await _publicationStore.FindAsync("publication-current");
        var storedCandidate = await _publicationStore.FindAsync(candidate.PublicationId);
        Assert.True(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.Equal(PublicationStatus.Active, result.Publication.Status);
        Assert.Equal("publication-failing", currentSlot!.ActiveActivationId);
        Assert.Equal("publication-failing", projectionPreparer.CurrentPublicationId);
        // The candidate's row did transition; only the predecessor's retirement is left diverged.
        Assert.Equal(PublicationStatus.Active, storedCandidate!.Status);
        Assert.Equal(PublicationStatus.Active, oldPublication!.Status);
        Assert.Null(oldPublication.RetiredAt);
    }

    /// <summary>
    /// T121. Unpublish holds no retraction sequence of its own any more, so this exercises the real
    /// <see cref="WorkflowActivationCoordinator"/> against real projection stores and asserts what the operator
    /// actually cares about: a failed unpublish leaves the workflow serving.
    /// </summary>
    /// <remarks>
    /// It deliberately does not stub the coordinator. The predecessor of this test asserted
    /// <c>preparer.Restored == true</c> against a hand-written <c>IPublicationProjectionPreparer</c> whose
    /// <c>RestoreAsync</c> set a flag and restored nothing — which is how the recurring-preparation defect stayed
    /// invisible. The assertions below read the serving projections back instead.
    /// </remarks>
    [Fact]
    public async Task UnpublishProjectionRemovalFailureRestoresAuthorityAndReplaysServingProjection()
    {
        var bindings = new InMemoryWorkflowTriggerBindingStore();
        var schedules = new FailingRecurringScheduleStore();
        var coordinator = NewCoordinator(bindings, schedules);
        var publication = await ActivateThroughCoordinatorAsync(coordinator, "publication-current");
        Assert.NotEmpty(await ServingBindingsAsync(bindings));
        Assert.NotEmpty(await schedules.ListDueAsync(_now.AddHours(2), 10));
        schedules.FailRemoval = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => NewUnpublishHandler(coordinator).Handle(
            new UnpublishPublicationSlot("definition-1", "default"),
            CancellationToken.None));

        var slot = await _slotStore.FindAsync("definition-1", "default");
        var stored = await _publicationStore.FindAsync(publication.PublicationId);
        Assert.Equal("publication-current", slot!.ActiveActivationId);
        Assert.Equal(3, slot.Revision);
        // The journal is retired only once the retraction succeeded, so a restored slot keeps an Active record.
        Assert.Equal(PublicationStatus.Active, stored!.Status);
        // Restored means SERVING, on both projections — the recurring one is the half a flag-setting stub used
        // to be able to lie about.
        Assert.NotEmpty(await ServingBindingsAsync(bindings));
        Assert.NotEmpty(await schedules.ListDueAsync(_now.AddHours(2), 10));
    }

    [Fact]
    public async Task UnpublishClearsTheSlotRetiresTheRecordAndStopsTheServingProjections()
    {
        var bindings = new InMemoryWorkflowTriggerBindingStore();
        var schedules = new FailingRecurringScheduleStore();
        var coordinator = NewCoordinator(bindings, schedules);
        var publication = await ActivateThroughCoordinatorAsync(coordinator, "publication-current");

        var slot = await NewUnpublishHandler(coordinator).Handle(
            new UnpublishPublicationSlot("definition-1", "default"),
            CancellationToken.None);

        Assert.Null(slot.ActiveActivationId);
        var stored = await _publicationStore.FindAsync(publication.PublicationId);
        Assert.Equal(PublicationStatus.Retired, stored!.Status);
        // Publishing still owns its own bookkeeping: the record and the source reference are retired here, by
        // the handler, with publishing's own reason — the runtime owns the slot and the projections.
        var reference = await _referenceStore.FindAsync(WorkflowActivationReferenceIdentity.Create("publication-current"));
        Assert.NotNull(reference!.DeletedAt);
        Assert.Equal("publication-unpublished", reference.DeletedReason);
        Assert.Empty(await ServingBindingsAsync(bindings));
        Assert.Empty(await schedules.ListDueAsync(_now.AddHours(2), 10));
    }

    /// <summary>
    /// T116 / FR-B-006: an activation minted by artifact reconciliation has no <see cref="PublicationRecord"/>,
    /// and that absence is an <em>ownership</em> answer — "not published by me" — never a missing-data fault.
    /// </summary>
    /// <remarks>
    /// The refusal carries <c>slot_owner_conflict</c>, the same code the authority's own
    /// <see cref="WorkflowActivationConflict.ForeignSource"/> maps to, and it names the owner so an operator is
    /// told who holds the definition. The slot assertions are the substantive half: publishing must not be able
    /// to deactivate what it did not activate, so nothing about the foreign activation may move.
    /// </remarks>
    [Fact]
    public async Task UnpublishRefusesAnImportOwnedSlotByNameInsteadOfReportingAMissingPublication()
    {
        const string importActivationId = "import:mounted-artifacts:artifact-1";
        var owner = WorkflowActivationSource.ArtifactReconciliation("mounted-artifacts");
        var activated = await _slotStore.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1",
            "default",
            importActivationId,
            owner,
            0,
            _now));
        Assert.True(activated.Succeeded);
        var handler = NewUnpublishHandler(NewCoordinator(new InMemoryWorkflowTriggerBindingStore(), new FailingRecurringScheduleStore()));

        var refusal = await Assert.ThrowsAsync<PublicationActivationException>(() => handler.Handle(
            new UnpublishPublicationSlot("definition-1", "default"),
            CancellationToken.None));

        Assert.Equal("slot_owner_conflict", refusal.Code);
        Assert.Contains(owner.Describe(), refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("does not exist", refusal.Message, StringComparison.Ordinal);

        var slot = await _slotStore.FindAsync("definition-1", "default");
        Assert.Equal(importActivationId, slot!.ActiveActivationId);
        Assert.Equal(owner.Kind, slot.Source!.Kind);
        Assert.Equal(owner.SourceId, slot.Source.SourceId);
        Assert.Equal(activated.Slot.Revision, slot.Revision);
    }

    private PublicationActivator NewActivator(
        IWorkflowTriggerIndexer triggerIndexer,
        IPublicationRecordStore? publicationStore = null,
        IWorkflowTriggerBindingStore? triggerBindingStore = null) =>
        new(
            new WorkflowActivationCoordinator(
                _slotStore,
                _referenceStore,
                TestRootWriteLeases.Create(_executableStore),
                new FixedTimeProvider(_now),
                triggerIndexer,
                triggerBindingStore ?? new NoopTriggerBindingStore()),
            publicationStore ?? _publicationStore,
            new FixedTimeProvider(_now));

    /// <summary>
    /// A real coordinator over real projection stores, for the retraction tests. Unpublish requests deactivation
    /// through this; it does not implement one (T121).
    /// </summary>
    private WorkflowActivationCoordinator NewCoordinator(
        IWorkflowTriggerBindingStore bindingStore,
        IRecurringTriggerScheduleStore scheduleStore) =>
        new(
            _slotStore,
            _referenceStore,
            TestRootWriteLeases.Create(_executableStore),
            new FixedTimeProvider(_now),
            new ServingProjectionIndexer(bindingStore, _now),
            bindingStore,
            scheduleStore,
            new ServingSchedulePreparer(scheduleStore, _now));

    private UnpublishPublicationSlotRequestHandler NewUnpublishHandler(IWorkflowActivationCoordinator coordinator) =>
        new(
            _slotStore,
            coordinator,
            _publicationStore,
            _executableStore,
            _referenceStore,
            new FixedTimeProvider(_now));

    /// <summary>Makes one publication live through the coordinator, the way a publish would.</summary>
    private async Task<PublicationRecord> ActivateThroughCoordinatorAsync(
        IWorkflowActivationCoordinator coordinator,
        string publicationId)
    {
        // The publish pipeline mints the record's SourceReferenceId from the activation id, because the
        // coordinator owns activation↔reference identity; mirror that or the record points at nothing.
        var record = Record(publicationId, expectedSlotRevision: 0, PublicationStatus.Active, activatedAt: _now) with
        {
            SourceReferenceId = WorkflowActivationReferenceIdentity.Create(publicationId)
        };
        await _publicationStore.SaveAsync(record);
        var executable = Executable(record);
        await _executableStore.SaveAsync(executable);
        var activation = await coordinator.ActivateAsync(new WorkflowActivationCommand(
            executable,
            Reference(record),
            "default",
            publicationId,
            WorkflowActivationSource.Publishing,
            0));
        Assert.True(activation.Succeeded);
        return record;
    }

    private static async Task<IReadOnlyCollection<WorkflowTriggerBinding>> ServingBindingsAsync(IWorkflowTriggerBindingStore store) =>
        (await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "orders"))).Items;

    /// <summary>
    /// The coordinator takes the executable's root-write lease, so the artifact has to exist in the store —
    /// the activator no longer runs outside one.
    /// </summary>
    private async Task<PublicationActivationRequest> RequestAsync(PublicationRecord candidate)
    {
        var executable = Executable(candidate);
        await _executableStore.SaveAsync(executable);
        return new(candidate, executable, Reference(candidate));
    }

    private static WorkflowExecutable Executable(PublicationRecord candidate) =>
        TestExecutable.Create(TestExecutable.Identity(
            candidate.ArtifactId,
            "sha256:hash",
            candidate.WorkflowDefinitionId,
            candidate.WorkflowDefinitionVersionId));

    private WorkflowExecutableSourceReference Reference(PublicationRecord candidate) =>
        new(
            SourceReferenceId: candidate.SourceReferenceId!,
            ArtifactId: candidate.ArtifactId,
            SourceKind: WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
            SourceId: candidate.WorkflowDefinitionVersionId,
            SourceVersion: "1.0.0",
            DefinitionId: candidate.WorkflowDefinitionId,
            DefinitionVersionId: candidate.WorkflowDefinitionVersionId,
            ArtifactVersion: "1.0.0",
            CreatedAt: _now,
            PublishedAt: _now,
            Scope: WorkflowExecutableReferenceScope.Published);

    private async Task SeedActivePublicationAsync(string publicationId)
    {
        var record = Record(
            publicationId,
            expectedSlotRevision: 0,
            PublicationStatus.Active,
            activatedAt: _now);
        await _publicationStore.SaveAsync(record);
        await _referenceStore.SaveAsync(Reference(record) with
        {
            SourceReferenceId = WorkflowActivationReferenceIdentity.Create(publicationId)
        });
        var activated = await _slotStore.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1",
            "default",
            publicationId,
            WorkflowActivationSource.Publishing,
            0,
            _now));

        Assert.True(activated.Succeeded);
    }

    private PublicationRecord Candidate(string publicationId, long expectedSlotRevision) =>
        Record(publicationId, expectedSlotRevision, PublicationStatus.Candidate);

    private PublicationRecord Record(
        string publicationId,
        long expectedSlotRevision,
        PublicationStatus status,
        DateTimeOffset? activatedAt = null) =>
        new(
            PublicationId: publicationId,
            SlotId: WorkflowActivationSlotIdentity.Create("definition-1", "default"),
            WorkflowDefinitionId: "definition-1",
            WorkflowDefinitionVersionId: $"version-{publicationId}",
            ArtifactId: $"artifact-{publicationId}",
            SourceReferenceId: $"reference-{publicationId}",
            ExpectedSlotRevision: expectedSlotRevision,
            Status: status,
            CreatedAt: _now,
            ActivatedAt: activatedAt,
            RetiredAt: null,
            Failure: null);

    /// <summary>Blocks the coordinator's prepare step until every concurrent candidate has reached it.</summary>
    private sealed class BarrierTriggerIndexer(int participantCount) : IWorkflowTriggerIndexer
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string publicationId,
            string slotId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrived) == participantCount)
                _allArrived.TrySetResult();

            await _allArrived.Task.WaitAsync(cancellationToken);
            return [];
        }
    }

    private sealed class ThrowingTriggerIndexer : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string publicationId,
            string slotId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IReadOnlyCollection<WorkflowTriggerBinding>>(
                new InvalidOperationException("Candidate trigger projection could not be prepared."));
    }

    private sealed class NoopTriggerIndexer : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string publicationId,
            string slotId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowTriggerBinding>>([]);
    }

    private class NoopTriggerBindingStore : IWorkflowTriggerBindingStore
    {
        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(binding);

        public virtual ValueTask ActivateAsync(string publicationId, string? replacedPublicationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DeleteByActivationAsync(string publicationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask PrepareActivationAsync(string publicationId, IReadOnlyCollection<WorkflowTriggerBinding> bindings, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(WorkflowTriggerBindingActivationPageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowTriggerBindingPage(query, [], 0, null));

        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowTriggerBindingPage(query, [], 0, null));

        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowTriggerBindingPage(query, [], 0, null));

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowTriggerBindingPage(query, [], 0, null));
    }

    /// <summary>Records which activation last had its serving projection made visible.</summary>
    private sealed class TrackingTriggerBindingStore(string? currentPublicationId) : NoopTriggerBindingStore
    {
        public string? CurrentPublicationId { get; private set; } = currentPublicationId;

        public override ValueTask ActivateAsync(string publicationId, string? replacedPublicationId, CancellationToken cancellationToken = default)
        {
            CurrentPublicationId = publicationId;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailOncePublicationRecordStore(
        IPublicationRecordStore inner,
        Func<PublicationRecord, PublicationStatus, bool> shouldFail) : IPublicationRecordStore
    {
        private int _failureInjected;

        public ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(publication, cancellationToken);

        public ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(publicationId, cancellationToken);

        public ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default) =>
            inner.ListBySlotAsync(slotId, cancellationToken);

        public ValueTask<bool> TryTransitionAsync(
            PublicationRecord publication,
            PublicationStatus expectedStatus,
            CancellationToken cancellationToken = default)
        {
            if (shouldFail(publication, expectedStatus) && Interlocked.Exchange(ref _failureInjected, 1) == 0)
                return ValueTask.FromResult(false);
            return inner.TryTransitionAsync(publication, expectedStatus, cancellationToken);
        }
    }

    /// <summary>
    /// Fails the recurring projection's removal only — the shape the old <c>PartialRemovalProjectionPreparer</c>
    /// described in a comment: the trigger bindings are already gone when the schedules refuse to follow, so
    /// nothing is left to simply re-activate and the coordinator has to re-prepare from the artifact.
    /// </summary>
    private sealed class FailingRecurringScheduleStore : IRecurringTriggerScheduleStore
    {
        private readonly InMemoryRecurringTriggerScheduleStore _inner = new();

        public bool FailRemoval { get; set; }

        public async ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
        {
            // Deletes and THEN throws. A store that refused before deleting would leave the schedules standing,
            // and an assertion that they are still serving afterwards would hold whether or not compensation
            // re-prepared anything — measuring nothing. Losing them first is what makes the replay observable.
            await _inner.DeleteByActivationAsync(activationId, cancellationToken);
            if (FailRemoval)
                throw new InvalidOperationException("Recurring schedule removal failed after trigger bindings were removed.");
        }

        public ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<RecurringTriggerSchedule> schedules, CancellationToken cancellationToken = default) =>
            _inner.PrepareActivationAsync(activationId, schedules, cancellationToken);

        public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(schedule, cancellationToken);

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByActivationPageAsync(RecurringTriggerScheduleActivationPageQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListByActivationPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(RecurringTriggerScheduleArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListByArtifactPageAsync(query, cancellationToken);

        public ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default) =>
            _inner.ActivateAsync(activationId, replacedActivationId, cancellationToken);

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

    /// <summary>Prepares one real trigger binding, and nothing else — the whole of what the indexer advertises.</summary>
    private sealed class ServingProjectionIndexer(IWorkflowTriggerBindingStore bindingStore, DateTimeOffset now) : IWorkflowTriggerIndexer
    {
        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string activationId,
            string slotId,
            CancellationToken cancellationToken = default)
        {
            var binding = new WorkflowTriggerBinding(
                WorkflowTriggerBinding.BuildId(activationId, executable.Identity.ArtifactId, "node-root", "orders"),
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
                ActivationId: activationId,
                SlotId: slotId,
                IsActive: false);
            await bindingStore.PrepareActivationAsync(activationId, [binding], cancellationToken);
            return [binding];
        }
    }

    /// <summary>
    /// Prepares one real recurring schedule, which the indexer above deliberately does not. Keeping the two
    /// obligations in separate doubles is what lets a lost recurring preparation be seen at all — a single double
    /// covering both is what reproduced the decorator T044b retired and hid the defect.
    /// </summary>
    private sealed class ServingSchedulePreparer(IRecurringTriggerScheduleStore scheduleStore, DateTimeOffset now)
        : IRecurringTriggerScheduleProjectionPreparer
    {
        public async ValueTask PrepareActivationAsync(
            WorkflowExecutable executable,
            string activationId,
            string slotId,
            CancellationToken cancellationToken = default)
        {
            var schedule = new RecurringTriggerSchedule(
                RecurringTriggerSchedule.BuildId(activationId, executable.Identity.ArtifactId, "node-root"),
                executable.Identity.ArtifactId,
                "node-root",
                "Event",
                "orders",
                RecurringScheduleKind.Interval,
                "PT1H",
                now.AddHours(1),
                now,
                ActivationId: activationId,
                SlotId: slotId,
                IsActive: false);
            await scheduleStore.PrepareActivationAsync(activationId, [schedule], cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
