using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;
using Microsoft.Extensions.Time.Testing;

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
            new FakeTimeProvider(_now));

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
            new FakeTimeProvider(_now));
        var refusal = await Assert.ThrowsAsync<PublicationActivationException>(() =>
            handler.Handle(new UnpublishPublicationSlot("definition-1", "default"), CancellationToken.None));

        Assert.Equal("slot_owner_conflict", refusal.Code);
        var slot = await _authority.FindAsync("definition-1", "default");
        Assert.Equal("import:artifact-1", slot!.ActiveActivationId);
        Assert.Equal(owner, slot.Source);
        Assert.Equal(activated.Slot.Revision, slot.Revision);
    }

    [Fact]
    public async Task UnpublishRetiresARecordThatIsStillACandidateAndTheActivePublicationItReplaced()
    {
        // A process that stopped after the slot transition: the slot names the candidate, which nothing has completed.
        await SeedAsync("publication-old", PublicationStatus.Active, ReferenceState.Retired);
        await SeedAsync("publication-new", PublicationStatus.Candidate, occupiesSlot: true);
        var handler = new UnpublishPublicationSlotRequestHandler(
            _authority,
            NewCoordinator(),
            _publications,
            _executables,
            _references,
            new FakeTimeProvider(_now));

        var slot = await handler.Handle(new UnpublishPublicationSlot("definition-1", "default"), CancellationToken.None);

        Assert.Null(slot.ActiveActivationId);
        var unpublished = await _publications.FindAsync("publication-new");
        Assert.Equal((PublicationStatus.Retired, (DateTimeOffset?)_now, (DateTimeOffset?)_now), (unpublished!.Status, unpublished.ActivatedAt, unpublished.RetiredAt));
        Assert.Equal(PublicationStatus.Retired, (await _publications.FindAsync("publication-old"))!.Status);
        Assert.Equal("publication-unpublished", (await _references.FindAsync(WorkflowActivationReferenceIdentity.Create("publication-new")))!.DeletedReason);
    }

    [Fact]
    public async Task CompletionMarksARetiredPublicationTheSlotNamesAgainActive()
    {
        await SeedAsync("publication-handed-back", PublicationStatus.Retired, occupiesSlot: true);

        var completion = await NewActivator().CompleteAsync("definition-1", "default");

        Assert.True(completion.Succeeded);
        var publication = await _publications.FindAsync("publication-handed-back");
        Assert.Equal(publication, completion.Publication);
        Assert.Equal((PublicationStatus.Active, _now, (DateTimeOffset?)null), (publication!.Status, publication.ActivatedAt, publication.RetiredAt));
    }

    [Fact]
    public async Task CompletionRetiresARetiredPublicationAgainWhenTheSlotMovedOnAfterItWasMarkedActive()
    {
        await SeedAsync("publication-handed-back", PublicationStatus.Retired, occupiesSlot: true);
        // The first read of the slot is the check that it serves; the second is the one after the mark.
        var activator = NewActivator(authority: new MovesSlotBeforeRead(_authority, readNumber: 2, () => OccupySlotAsync("publication-successor")));

        var completion = await activator.CompleteAsync("definition-1", "default");

        Assert.True(completion.Succeeded);
        var publication = await _publications.FindAsync("publication-handed-back");
        Assert.Equal(publication, completion.Publication);
        Assert.Equal((PublicationStatus.Retired, (DateTimeOffset?)_now, (DateTimeOffset?)_now), (publication!.Status, publication.ActivatedAt, publication.RetiredAt));
    }

    [Fact]
    public async Task CompletionLeavesACandidateAloneWhenTheSlotMovesDuringIt()
    {
        await SeedAsync("publication-new", PublicationStatus.Candidate, occupiesSlot: true);
        await SeedAsync("publication-old", PublicationStatus.Active);
        var activator = NewActivator(authority: new MovesSlotBeforeRead(_authority, readNumber: 1, () => OccupySlotAsync("publication-successor")));

        var completion = await activator.CompleteAsync("definition-1", "default");

        Assert.True(completion.Succeeded);
        Assert.Equal(PublicationStatus.Candidate, completion.Publication!.Status);
        Assert.Equal(PublicationStatus.Candidate, (await _publications.FindAsync("publication-new"))!.Status);
        Assert.Equal(PublicationStatus.Active, (await _publications.FindAsync("publication-old"))!.Status);
    }

    [Fact]
    public async Task CompletionLeavesTheJournalOfASlotAnotherSourceOwnsAlone()
    {
        await SeedAsync("import:artifact-1", PublicationStatus.Candidate, occupiesSlot: true, source: WorkflowActivationSource.ArtifactReconciliation("mounted-artifacts"));

        var completion = await NewActivator().CompleteAsync("definition-1", "default");

        Assert.True(completion.Succeeded);
        Assert.Null(completion.Publication);
        Assert.Equal(PublicationStatus.Candidate, (await _publications.FindAsync("import:artifact-1"))!.Status);
    }

    [Theory]
    [InlineData(PublicationStatus.Failed, ReferenceState.Live)]
    [InlineData(PublicationStatus.Candidate, ReferenceState.RetiredByFailedActivation)]
    [InlineData(PublicationStatus.Candidate, ReferenceState.Retired)]
    [InlineData(PublicationStatus.Candidate, ReferenceState.Missing)]
    public async Task CompletionLeavesAPublicationThatCannotServeAlone(PublicationStatus status, ReferenceState reference)
    {
        var before = await SeedAsync("publication-new", status, reference, occupiesSlot: true);
        await SeedAsync("publication-old", PublicationStatus.Active, ReferenceState.Retired);

        var completion = await NewActivator().CompleteAsync("definition-1", "default");

        Assert.True(completion.Succeeded);
        Assert.Equal(before, await _publications.FindAsync("publication-new"));
        Assert.Equal(PublicationStatus.Active, (await _publications.FindAsync("publication-old"))!.Status);
    }

    [Theory]
    [InlineData(ReferenceState.Live, PublicationStatus.Active)]
    [InlineData(ReferenceState.RetiredByFailedActivation, PublicationStatus.Active)]
    [InlineData(ReferenceState.Retired, PublicationStatus.Retired)]
    [InlineData(ReferenceState.Missing, PublicationStatus.Retired)]
    public async Task CompletionRetiresAnotherActivePublicationOnlyWhenTheRuntimeRetiredItsActivation(ReferenceState siblingReference, PublicationStatus expected)
    {
        await SeedAsync("publication-sibling", PublicationStatus.Active, siblingReference);
        await SeedAsync("publication-new", PublicationStatus.Candidate, occupiesSlot: true);

        await NewActivator().CompleteAsync("definition-1", "default");

        Assert.Equal(PublicationStatus.Active, (await _publications.FindAsync("publication-new"))!.Status);
        Assert.Equal(expected, (await _publications.FindAsync("publication-sibling"))!.Status);
    }

    [Fact]
    public async Task ParallelCompletionsConvergeToOneJournalState()
    {
        await SeedAsync("publication-old", PublicationStatus.Active, ReferenceState.Retired);
        await SeedAsync("publication-new", PublicationStatus.Candidate, occupiesSlot: true);
        var activator = NewActivator();

        var completions = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => activator.CompleteAsync("definition-1", "default").AsTask())));

        Assert.All(completions, completion => Assert.True(completion.Succeeded));
        Assert.All(completions, completion => Assert.Equal("publication-new", completion.Publication!.PublicationId));
        Assert.Equal(PublicationStatus.Active, (await _publications.FindAsync("publication-new"))!.Status);
        Assert.Equal(PublicationStatus.Retired, (await _publications.FindAsync("publication-old"))!.Status);
    }

    [Fact]
    public async Task UnpublishLeavesAPublicationThatWonTheSlotAfterTheDeactivationActive()
    {
        await SeedAsync("publication-current", PublicationStatus.Active, occupiesSlot: true);
        // A publish that won the slot in between: active with a live reference, and the slot no longer names the record.
        var winner = await SeedAsync("publication-winner", PublicationStatus.Active);

        await NewUnpublisher().Handle(new UnpublishPublicationSlot("definition-1", "default"), CancellationToken.None);

        Assert.Equal(PublicationStatus.Retired, (await _publications.FindAsync("publication-current"))!.Status);
        Assert.Equal(winner, await _publications.FindAsync("publication-winner"));
    }

    [Fact]
    public async Task UnpublishLeavesRecordsOfAnotherSlotAndNonActiveSiblingsAlone()
    {
        await SeedAsync("publication-current", PublicationStatus.Active, occupiesSlot: true);
        var failed = await SeedAsync("publication-failed", PublicationStatus.Failed, ReferenceState.Retired);
        var candidate = await SeedAsync("publication-candidate", PublicationStatus.Candidate, ReferenceState.Retired);
        var otherSlot = Record("publication-other-slot", 0, PublicationStatus.Active, _now) with
        {
            SlotId = WorkflowActivationSlotIdentity.Create("definition-1", "other"),
            SlotName = "other"
        };
        await _publications.SaveAsync(otherSlot);

        await NewUnpublisher().Handle(new UnpublishPublicationSlot("definition-1", "default"), CancellationToken.None);

        Assert.Equal(PublicationStatus.Retired, (await _publications.FindAsync("publication-current"))!.Status);
        Assert.Equal(failed, await _publications.FindAsync("publication-failed"));
        Assert.Equal(candidate, await _publications.FindAsync("publication-candidate"));
        Assert.Equal(otherSlot, await _publications.FindAsync("publication-other-slot"));
    }

    private UnpublishPublicationSlotRequestHandler NewUnpublisher() =>
        new(_authority, NewCoordinator(), _publications, _executables, _references, new FakeTimeProvider(_now));

    private PublicationActivator NewActivator(IWorkflowTriggerIndexer? indexer = null, IWorkflowActivationAuthority? authority = null) =>
        new(NewCoordinator(indexer), _publications, authority ?? _authority, _references, new FakeTimeProvider(_now));

    private WorkflowActivationCoordinator NewCoordinator(IWorkflowTriggerIndexer? indexer = null) =>
        new(
            _authority,
            _references,
            TestRootWriteLeases.Create(_executables),
            new FakeTimeProvider(_now),
            indexer ?? new NoopTriggerIndexer(),
            new NoopTriggerBindingStore());

    private async Task<PublicationActivationRequest> RequestAsync(PublicationRecord candidate)
    {
        var executable = Executable(candidate);
        await _executables.SaveAsync(executable);
        return new(candidate, executable, Reference(candidate));
    }

    private static readonly string SlotId = WorkflowActivationSlotIdentity.Create("definition-1", "default");

    /// <summary>What the runtime has done to a publication's source reference.</summary>
    public enum ReferenceState
    {
        Live,
        Retired,
        RetiredByFailedActivation,
        Missing
    }

    /// <summary>Seeds a record, its executable and its source reference, and optionally has the slot name it.</summary>
    private async Task<PublicationRecord> SeedAsync(
        string publicationId,
        PublicationStatus status,
        ReferenceState reference = ReferenceState.Live,
        bool occupiesSlot = false,
        WorkflowActivationSource? source = null)
    {
        var record = Record(publicationId, 0, status, status is PublicationStatus.Active or PublicationStatus.Retired ? _now : null) with
        {
            RetiredAt = status == PublicationStatus.Retired ? _now : null
        };
        await _publications.SaveAsync(record);
        await _executables.SaveAsync(Executable(record));
        if (reference != ReferenceState.Missing)
        {
            await _references.SaveAsync(Reference(record));
            if (reference != ReferenceState.Live)
                await _references.RetireAsync(
                    record.SourceReferenceId!,
                    _now,
                    reference == ReferenceState.RetiredByFailedActivation ? WorkflowActivationCoordinator.FailedRetireReason : "activation-replaced");
        }

        if (occupiesSlot)
            await OccupySlotAsync(publicationId, source);
        return record;
    }

    private async Task OccupySlotAsync(string publicationId, WorkflowActivationSource? source = null)
    {
        var revision = (await _authority.FindAsync("definition-1", "default"))?.Revision ?? 0;
        var transition = await _authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1", "default", publicationId, source ?? WorkflowActivationSource.Publishing, revision, _now));
        Assert.True(transition.Succeeded);
    }

    /// <summary>Moves the slot just before the authority's <c>readNumber</c>th read, as another node's activation would.</summary>
    private sealed class MovesSlotBeforeRead(IWorkflowActivationAuthority inner, int readNumber, Func<Task> move) : IWorkflowActivationAuthority
    {
        private int _reads;

        public async ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _reads) == readNumber)
                await move();
            return await inner.FindAsync(workflowDefinitionId, slotName, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            inner.ListByDefinitionAsync(workflowDefinitionId, cancellationToken);

        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
            inner.TryActivateAsync(request, cancellationToken);

        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
            string workflowDefinitionId,
            string slotName,
            WorkflowActivationSource source,
            long expectedRevision,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            inner.TryDeactivateAsync(workflowDefinitionId, slotName, source, expectedRevision, updatedAt, cancellationToken);
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

        public ValueTask<WorkflowActivationProjectionState> FindActivationStateAsync(
            string activationId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(WorkflowActivationProjectionState.Active);

        public ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(
            string slotId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<string>>([]);

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

        public ValueTask<IReadOnlyCollection<string>> ListActiveStimulusHashesAsync(
            string stimulusType,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<string>>([]);

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(
            WorkflowTriggerBindingTypePageQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowTriggerBindingPage(query, [], 0, null));
    }
}
