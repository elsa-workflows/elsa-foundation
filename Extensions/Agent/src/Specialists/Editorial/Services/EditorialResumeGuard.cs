using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Agent.Specialists.Editorial.Services;

/// <summary>
/// Validates an editorial decision before native admission claims the review wait, following the
/// DispatchWorkflow guard. A fresh decision must be authentic, target this exact pinned wait, be unexpired
/// and come from a subject who holds the decide permission right now. A durably claimed replay of the same
/// decision is historical fact and is finished without re-checking expiry or permission. A temporary
/// dependency failure keeps the resume owed; any other refusal leaves the valid pending wait in place.
/// </summary>
internal sealed class EditorialResumeGuard(EditorialDecisionCodec codec, ICurrentSubjectPermissionAuthorizer subjects, TimeProvider time)
    : IEventHandler<ActivityResumeValidating>
{
    public async Task Handle(ActivityResumeValidating @event, CancellationToken cancellationToken)
    {
        var state = @event.State;
        var payload = @event.Payload;
        if (@event.Node.ActivityType != EditorialReviewIds.ActivityType) return;
        try
        {
            if (!EditorialReviewRecords.IsDecisionMetadata(payload.TriggerDelivery))
                throw EditorialReviewException.Invalid("An editorial review resumes only through a typed editorial decision.");
            var review = EditorialReviewRecords.ReadState(state);
            var claims = EditorialReviewRecords.ReadDelivery(codec, payload.Input);
            if (payload.ActivityExecutionId != state.InvocationId || payload.StimulusType != EditorialReviewIds.StimulusType ||
                payload.StimulusHash != review.WaitId || !claims.Targets(review, payload.BookmarkId))
                throw EditorialReviewException.Invalid("The decision does not target this exact article and wait.");
            if (EditorialReviewRecords.IsClaimedReplay(state, payload)) return;
            if (EditorialReviewRecords.ConsumedDecision(state) is not null)
                throw EditorialReviewException.Refused("editorial.review.resolved", "The review already admitted another decision.");
            if (time.GetUtcNow() >= claims.ExpiresAt)
                throw EditorialReviewException.Refused("editorial.review.expired", "The decision expired before it was admitted.");
            var current = await subjects.AuthorizeAsync(
                new(claims.TenantId, claims.ActorId, EditorialReviewIds.DecidePermission), cancellationToken);
            if (current.Status != CurrentSubjectPermissionStatus.Granted)
                throw EditorialReviewException.Refused("editorial.review.forbidden", "The decider no longer holds the review permission.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (EditorialReviewException exception) when (exception.IsUnavailable) { throw Unavailable(exception); }
        catch (EditorialReviewException) { throw; }
        catch (Exception exception)
        {
            // Identity or store reads that fail are not a refusal: the decision stays owed until they recover.
            throw Unavailable(exception);
        }
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable(Exception cause) =>
        new("editorial.review.unavailable", "The editorial decision could not be validated now; resume remains owed.", cause);
}
