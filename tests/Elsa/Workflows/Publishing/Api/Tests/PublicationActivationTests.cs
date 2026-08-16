using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
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

    [Fact]
    public async Task UnpublishProjectionRemovalFailureRestoresAuthorityAndReplaysServingProjection()
    {
        await SeedActivePublicationAsync("publication-current");
        var projectionPreparer = new PartialRemovalProjectionPreparer();
        var handler = new UnpublishPublicationSlotRequestHandler(
            _slotStore,
            _publicationStore,
            projectionPreparer,
            new InMemoryWorkflowExecutableSourceReferenceStore(),
            new FixedTimeProvider(_now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UnpublishPublicationSlot("definition-1", "default"),
            CancellationToken.None));

        var slot = await _slotStore.FindAsync("definition-1", "default");
        var publication = await _publicationStore.FindAsync("publication-current");
        Assert.Equal("publication-current", slot!.ActiveActivationId);
        Assert.Equal(3, slot.Revision);
        Assert.Equal(PublicationStatus.Active, publication!.Status);
        Assert.True(projectionPreparer.Restored);
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

        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(
            WorkflowExecutable executable,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowTriggerBinding>>([]);

        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PreparePublicationAsync(
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
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(
            WorkflowExecutable executable,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IReadOnlyCollection<WorkflowTriggerBinding>>(
                new InvalidOperationException("Candidate trigger projection could not be prepared."));

        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PreparePublicationAsync(
            WorkflowExecutable executable,
            string publicationId,
            string slotId,
            CancellationToken cancellationToken = default) =>
            IndexAsync(executable, cancellationToken);
    }

    private sealed class NoopTriggerIndexer : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(
            WorkflowExecutable executable,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowTriggerBinding>>([]);

        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PreparePublicationAsync(
            WorkflowExecutable executable,
            string publicationId,
            string slotId,
            CancellationToken cancellationToken = default) =>
            IndexAsync(executable, cancellationToken);
    }

    private class NoopTriggerBindingStore : IWorkflowTriggerBindingStore
    {
        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(binding);

        public virtual ValueTask ActivatePublicationAsync(string publicationId, string? replacedPublicationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DeleteByPublicationAsync(string publicationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask PreparePublicationAsync(string publicationId, IReadOnlyCollection<WorkflowTriggerBinding> bindings, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<WorkflowTriggerBindingPage> ListByPublicationAsync(WorkflowTriggerBindingPublicationPageQuery query, CancellationToken cancellationToken = default) =>
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

        public override ValueTask ActivatePublicationAsync(string publicationId, string? replacedPublicationId, CancellationToken cancellationToken = default)
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

    private sealed class PartialRemovalProjectionPreparer : IPublicationProjectionPreparer
    {
        public bool Restored { get; private set; }

        public ValueTask PrepareAsync(PublicationRecord candidate, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ActivateAsync(PublicationRecord candidate, string? replacedPublicationId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CompensateAsync(PublicationRecord candidate, string? restoredPublicationId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask RestoreAsync(PublicationRecord publication, CancellationToken cancellationToken = default)
        {
            Restored = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(PublicationRecord publication, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException("Recurring schedule removal failed after trigger bindings were removed."));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
