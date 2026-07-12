using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// RED contract for ADR 0043's prepared-first, revisioned publication activation boundary.
/// </summary>
public sealed class PublicationActivationTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryPublicationSlotStore _slotStore = new();
    private readonly InMemoryPublicationRecordStore _publicationStore = new();

    [Fact]
    public async Task ConcurrentCandidatesFromTheSameObservedRevisionHaveExactlyOneWinner()
    {
        await SeedActivePublicationAsync("publication-current");
        var projectionPreparer = new BarrierProjectionPreparer(participantCount: 2);
        var activator = NewActivator(projectionPreparer);
        var firstCandidate = Candidate("publication-first", expectedSlotRevision: 1);
        var secondCandidate = Candidate("publication-second", expectedSlotRevision: 1);

        var results = await Task.WhenAll(
            activator.ActivateAsync(new PublicationActivationRequest(firstCandidate)).AsTask(),
            activator.ActivateAsync(new PublicationActivationRequest(secondCandidate)).AsTask());

        var winner = Assert.Single(results.Where(result => result.Succeeded));
        var loser = Assert.Single(results.Where(result => !result.Succeeded));
        var currentSlot = await _slotStore.FindAsync("definition-1", "default");
        var oldPublication = await _publicationStore.FindAsync("publication-current");
        var losingPublication = await _publicationStore.FindAsync(
            currentSlot!.ActivePublicationId == firstCandidate.PublicationId
                ? secondCandidate.PublicationId
                : firstCandidate.PublicationId);

        Assert.Equal(2, currentSlot.Revision);
        Assert.Contains(currentSlot.ActivePublicationId, new[] { firstCandidate.PublicationId, secondCandidate.PublicationId });
        Assert.Equal(currentSlot.ActivePublicationId, winner.Publication.PublicationId);
        Assert.Equal("slot_revision_conflict", loser.Failure?.Code);
        Assert.Equal(PublicationStatus.Retired, oldPublication!.Status);
        Assert.Equal(PublicationStatus.Failed, losingPublication!.Status);
    }

    [Fact]
    public async Task ProjectionPreparationFailureLeavesPriorAuthorityAndServingStateUntouched()
    {
        await SeedActivePublicationAsync("publication-current");
        var activator = NewActivator(new ThrowingProjectionPreparer());
        var candidate = Candidate("publication-failing", expectedSlotRevision: 1);

        var result = await activator.ActivateAsync(new PublicationActivationRequest(candidate));

        var currentSlot = await _slotStore.FindAsync("definition-1", "default");
        var oldPublication = await _publicationStore.FindAsync("publication-current");
        var failedCandidate = await _publicationStore.FindAsync(candidate.PublicationId);

        Assert.False(result.Succeeded);
        Assert.Equal("projection_preparation_failed", result.Failure?.Code);
        Assert.Equal("publication-current", currentSlot!.ActivePublicationId);
        Assert.Equal(1, currentSlot.Revision);
        Assert.Equal(PublicationStatus.Active, oldPublication!.Status);
        Assert.Null(oldPublication.RetiredAt);
        Assert.Equal(PublicationStatus.Failed, failedCandidate!.Status);
        Assert.NotNull(failedCandidate.Failure);
    }

    private PublicationActivator NewActivator(IPublicationProjectionPreparer projectionPreparer) =>
        new(
            _slotStore,
            _publicationStore,
            projectionPreparer,
            new FixedTimeProvider(_now));

    private async Task SeedActivePublicationAsync(string publicationId)
    {
        await _publicationStore.SaveAsync(Record(
            publicationId,
            expectedSlotRevision: 0,
            PublicationStatus.Active,
            activatedAt: _now));
        var activated = await _slotStore.TryActivateAsync(
            "definition-1",
            "default",
            publicationId,
            expectedRevision: 0,
            _now);

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
            SlotId: PublicationSlotIdentity.Create("definition-1", "default"),
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

    private sealed class BarrierProjectionPreparer(int participantCount) : IPublicationProjectionPreparer
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async ValueTask PrepareAsync(PublicationRecord candidate, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrived) == participantCount)
                _allArrived.TrySetResult();

            await _allArrived.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ThrowingProjectionPreparer : IPublicationProjectionPreparer
    {
        public ValueTask PrepareAsync(PublicationRecord candidate, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException("Candidate trigger projection could not be prepared."));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
