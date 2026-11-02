using Elsa.Agent.Specialists.Review.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Agent.Specialists.Review.Services;

/// <summary>
/// Validates a human review decision before native admission claims the review wait, following the
/// DispatchWorkflow guard. A fresh decision must be authentic, target this exact pinned wait, be unexpired
/// and come from a subject who holds the decide permission right now. A durably claimed replay of the same
/// decision is historical fact and is finished without re-checking expiry or permission. A temporary
/// dependency failure keeps the resume owed; any other refusal leaves the valid pending wait in place.
/// </summary>
internal sealed class HumanReviewResumeGuard(ReviewDecisionCodec codec, ICurrentSubjectPermissionAuthorizer subjects, TimeProvider time)
    : IEventHandler<ActivityResumeValidating>
{
    public async Task Handle(ActivityResumeValidating @event, CancellationToken cancellationToken)
    {
        var state = @event.State;
        var payload = @event.Payload;
        if (@event.Node.ActivityType != HumanReviewIds.ActivityType) return;
        try
        {
            if (!HumanReviewRecords.IsDecisionMetadata(payload.TriggerDelivery))
                throw HumanReviewException.Invalid("A human review resumes only through a typed review decision.");
            var review = HumanReviewRecords.ReadState(state);
            var claims = HumanReviewRecords.ReadDelivery(codec, payload.Input);
            if (payload.ActivityExecutionId != state.InvocationId || payload.StimulusType != HumanReviewIds.StimulusType ||
                payload.StimulusHash != review.WaitId || !claims.Targets(review, payload.BookmarkId))
                throw HumanReviewException.Invalid("The decision does not target this exact content and wait.");
            if (HumanReviewRecords.IsClaimedReplay(state, payload)) return;
            if (HumanReviewRecords.ConsumedDecision(state) is not null)
                throw HumanReviewException.Refused("agent.review.resolved", "The review already admitted another decision.");
            if (time.GetUtcNow() >= claims.ExpiresAt)
                throw HumanReviewException.Refused("agent.review.expired", "The decision expired before it was admitted.");
            var current = await subjects.AuthorizeAsync(
                new(claims.TenantId, claims.ActorId, HumanReviewIds.DecidePermission), cancellationToken);
            if (current.Status != CurrentSubjectPermissionStatus.Granted)
                throw HumanReviewException.Refused("agent.review.forbidden", "The decider no longer holds the review permission.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HumanReviewException exception) when (exception.IsUnavailable) { throw Unavailable(exception); }
        catch (HumanReviewException) { throw; }
        catch (RuntimeSchedulerWorkUnavailableException) { throw; }
        catch (Exception exception) when (IsTransient(exception))
        {
            // An identity or store read that fails is not a refusal: the decision stays owed until it recovers.
            throw Unavailable(exception);
        }
        // Anything else is deterministic and poisons the work item, leaving the pending wait for an operator.
    }

    private static bool IsTransient(Exception exception) =>
        exception is TimeoutException or IOException or System.Data.Common.DbException or HttpRequestException or OperationCanceledException ||
        exception.InnerException is { } inner && IsTransient(inner);

    private static RuntimeSchedulerWorkUnavailableException Unavailable(Exception cause) =>
        new("agent.review.unavailable", "The review decision could not be validated now; resume remains owed.", cause);
}
