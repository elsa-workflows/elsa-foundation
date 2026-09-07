using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>Public behavior tests for Publishing's runtime-owned activation boundary.</summary>
public sealed class PublicationActivationTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowActivationAuthority _authority = new();
    private readonly InMemoryPublicationRecordStore _publications = new();
    private readonly InMemoryWorkflowExecutableStore _executables = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _references = new();

    [Fact]
    public async Task ConcurrentCandidatesFromTheSameObservedRevisionHaveExactlyOneWinner()
    {
        await SeedActivePublicationAsync("publication-current");
        var activator = NewActivator();
        var first = Candidate("publication-first", expectedSlotRevision: 1);
        var second = Candidate("publication-second", expectedSlotRevision: 1);

        var results = await Task.WhenAll(
            activator.ActivateAsync(await RequestAsync(first)).AsTask(),
            activator.ActivateAsync(await RequestAsync(second)).AsTask());

        Assert.True(results.Any(result => result.Succeeded), string.Join(" | ", results.Select(result =>
            $"{result.Succeeded}:{result.Failure?.Code}:{result.Failure?.Message}")));
        var winner = Assert.Single(results, result => result.Succeeded);
        var loser = Assert.Single(results, result => !result.Succeeded);
        var slot = await _authority.FindAsync("definition-1", "default");
        var old = await _publications.FindAsync("publication-current");
        var losingId = slot!.ActiveActivationId == first.PublicationId ? second.PublicationId : first.PublicationId;
        var losing = await _publications.FindAsync(losingId);

        Assert.Equal(2, slot.Revision);
        Assert.Equal(slot.ActiveActivationId, winner.Publication.PublicationId);
        Assert.Equal("slot_revision_conflict", loser.Failure?.Code);
        Assert.Equal(PublicationStatus.Retired, old!.Status);
        Assert.Equal(PublicationStatus.Failed, losing!.Status);
    }

    [Fact]
    public async Task ProjectionPreparationFailureLeavesPriorAuthorityUntouched()
    {
        await SeedActivePublicationAsync("publication-current");
        var activator = NewActivator(new ThrowingTriggerIndexer());
        var candidate = Candidate("publication-failing", expectedSlotRevision: 1);

        var result = await activator.ActivateAsync(await RequestAsync(candidate));

        var slot = await _authority.FindAsync("definition-1", "default");
        var current = await _publications.FindAsync("publication-current");
        var failed = await _publications.FindAsync(candidate.PublicationId);
        Assert.False(result.Succeeded);
        Assert.Equal("projection_preparation_failed", result.Failure?.Code);
        Assert.Equal("publication-current", slot!.ActiveActivationId);
        Assert.Equal(1, slot.Revision);
        Assert.Equal(PublicationStatus.Active, current!.Status);
        Assert.Equal(PublicationStatus.Failed, failed!.Status);
    }

    [Fact]
    public async Task UnpublishClearsAuthorityRetiresJournalAndReference()
    {
        await SeedActivePublicationAsync("publication-current");
        var handler = new UnpublishPublicationSlotRequestHandler(
            _authority,
            NewCoordinator(),
            _publications,
            _executables,
            _references,
            new FixedTimeProvider(_now));

        var slot = await handler.Handle(new UnpublishPublicationSlot("definition-1", "default"), CancellationToken.None);

        var publication = await _publications.FindAsync("publication-current");
        var reference = await _references.FindAsync(WorkflowActivationReferenceIdentity.Create("publication-current"));
        Assert.Null(slot.ActiveActivationId);
        Assert.Equal(PublicationStatus.Retired, publication!.Status);
        Assert.Equal("publication-unpublished", reference!.DeletedReason);
    }

    [Fact]
    public async Task UnpublishRefusesForeignActivationWithoutMovingAuthority()
    {
        var owner = WorkflowActivationSource.ArtifactReconciliation("mounted-artifacts");
        var activated = await _authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1", "default", "import:artifact-1", owner, 0, _now));
        Assert.True(activated.Succeeded);

        var handler = new UnpublishPublicationSlotRequestHandler(
            _authority,
            NewCoordinator(),
            _publications,
            _executables,
            _references,
            new FixedTimeProvider(_now));
        var refusal = await Assert.ThrowsAsync<PublicationActivationException>(() =>
            handler.Handle(new UnpublishPublicationSlot("definition-1", "default"), CancellationToken.None));

        Assert.Equal("slot_owner_conflict", refusal.Code);
        var slot = await _authority.FindAsync("definition-1", "default");
        Assert.Equal("import:artifact-1", slot!.ActiveActivationId);
        Assert.Equal(owner, slot.Source);
        Assert.Equal(activated.Slot.Revision, slot.Revision);
    }

    private PublicationActivator NewActivator(IWorkflowTriggerIndexer? indexer = null) =>
        new(NewCoordinator(indexer), _publications, new FixedTimeProvider(_now));

    private WorkflowActivationCoordinator NewCoordinator(IWorkflowTriggerIndexer? indexer = null) =>
        new(
            _authority,
            _references,
            TestRootWriteLeases.Create(_executables),
            new FixedTimeProvider(_now),
            indexer ?? new NoopTriggerIndexer(),
            new NoopTriggerBindingStore());

    private async Task<PublicationActivationRequest> RequestAsync(PublicationRecord candidate)
    {
        var executable = Executable(candidate);
        await _executables.SaveAsync(executable);
        return new(candidate, executable, Reference(candidate));
    }

    private async Task SeedActivePublicationAsync(string publicationId)
    {
        var record = Record(publicationId, 0, PublicationStatus.Active, _now);
        await _publications.SaveAsync(record);
        await _references.SaveAsync(Reference(record));
        await _executables.SaveAsync(Executable(record));
        var transition = await _authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1", "default", publicationId, WorkflowActivationSource.Publishing, 0, _now));
        Assert.True(transition.Succeeded);
    }

    private PublicationRecord Candidate(string publicationId, long expectedSlotRevision) =>
        Record(publicationId, expectedSlotRevision, PublicationStatus.Candidate, activatedAt: null);

    private PublicationRecord Record(string publicationId, long expectedSlotRevision, PublicationStatus status, DateTimeOffset? activatedAt) =>
        new(
            publicationId,
            WorkflowActivationSlotIdentity.Create("definition-1", "default"),
            "definition-1",
            $"version-{publicationId}",
            $"artifact-{publicationId}",
            WorkflowActivationReferenceIdentity.Create(publicationId),
            expectedSlotRevision,
            status,
            _now,
            activatedAt,
            RetiredAt: null,
            Failure: null,
            SlotName: "default");

    private static WorkflowExecutable Executable(PublicationRecord record) =>
        TestExecutable.Create(TestExecutable.Identity(
            record.ArtifactId,
            "sha256:hash",
            record.WorkflowDefinitionId,
            record.WorkflowDefinitionVersionId));

    private WorkflowExecutableSourceReference Reference(PublicationRecord record) =>
        new(
            record.SourceReferenceId!,
            record.ArtifactId,
            WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
            record.WorkflowDefinitionVersionId,
            "1.0.0",
            record.WorkflowDefinitionId,
            record.WorkflowDefinitionVersionId,
            "1.0.0",
            _now,
            _now,
            WorkflowExecutableReferenceScope.Published,
            ActivationId: record.PublicationId,
            SlotId: record.SlotId);

    private sealed class NoopTriggerIndexer : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(
            WorkflowExecutable executable,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowTriggerBinding>>([]);

        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string activationId,
            string slotId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowTriggerBinding>>([]);
    }

    private sealed class ThrowingTriggerIndexer : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(
            WorkflowExecutable executable,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IReadOnlyCollection<WorkflowTriggerBinding>>(
                new InvalidOperationException("Candidate trigger projection could not be prepared."));

        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string activationId,
            string slotId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IReadOnlyCollection<WorkflowTriggerBinding>>(
                new InvalidOperationException("Candidate trigger projection could not be prepared."));
    }

    private sealed class NoopTriggerBindingStore : IWorkflowTriggerBindingStore
    {
        public ValueTask<WorkflowTriggerBinding> SaveAsync(
            WorkflowTriggerBinding binding,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(binding);

        public ValueTask PrepareActivationAsync(
            string activationId,
            IReadOnlyCollection<WorkflowTriggerBinding> bindings,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ActivateAsync(
            string activationId,
            string? replacedActivationId,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DeleteByActivationAsync(
            string activationId,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<int> DeleteByArtifactAsync(
            string artifactId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(0);

        public ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(
            WorkflowTriggerBindingActivationPageQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowTriggerBindingPage(query, [], 0, null));

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(
            WorkflowTriggerBindingPageQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowTriggerBindingPage(query, [], 0, null));

        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(
            WorkflowTriggerBindingArtifactPageQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowTriggerBindingPage(query, [], 0, null));

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(
            WorkflowTriggerBindingTypePageQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowTriggerBindingPage(query, [], 0, null));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
