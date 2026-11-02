using Elsa.Agent.Specialists.Review.Models;

namespace Elsa.Agent.Specialists.Review.Contracts;

/// <summary>
/// Reads and decides one exact human review wait. Callers supply the server-derived tenant and actor;
/// nothing in a command confers authority. This is content approval only and grants no tool or effect permission.
/// </summary>
public interface IHumanReviews
{
    ValueTask<HumanReviewView?> GetAsync(HumanReviewReference reference, ReviewDecider caller, CancellationToken cancellationToken = default);

    ValueTask<ReviewDecisionReceipt> DecideAsync(ReviewDecisionCommand command, ReviewDecider decider, CancellationToken cancellationToken = default);
}

public sealed record HumanReviewReference(string WorkflowExecutionId, string ActivityExecutionId);

/// <summary>The authenticated caller, derived by the host from its normalized principal.</summary>
public sealed record ReviewDecider(string TenantId, string ActorId);

public sealed record ReviewDecisionCommand(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ContentHash,
    ReviewDecision Decision,
    string? Comment,
    string RequestId);

public enum HumanReviewStatus
{
    Pending,
    Approved,
    Rejected,
    Closed
}

public sealed record HumanReviewView(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string? Title,
    System.Text.Json.JsonElement Content,
    string? Notes,
    string ContentHash,
    HumanReviewStatus Status,
    string? DecidedBy,
    string? DecisionComment,
    string? RequestId);

public enum ReviewDecisionReceiptStatus
{
    /// <summary>This request's decision was admitted; repeating it replays this receipt.</summary>
    Accepted,

    /// <summary>The decision is queued for native admission; read the review to see it resolve.</summary>
    Pending,

    /// <summary>Another decision was already admitted for this wait.</summary>
    AlreadyResolved,

    /// <summary>The content changed, or this request ID already carries different content.</summary>
    Conflict,

    /// <summary>The caller does not currently hold the decide permission.</summary>
    Forbidden,

    /// <summary>No visible pending review exists for this caller.</summary>
    NotFound,

    /// <summary>The review ended without a decision, for example through cancellation.</summary>
    Closed,

    /// <summary>A dependency was temporarily unavailable; retry with the same request ID.</summary>
    Unavailable,

    /// <summary>
    /// Native admission refused this request (for example expiry or revoked permission); the wait stays open. Submit a
    /// new request ID. A refusal is not a durable receipt: once the runtime forgets the request (a restart, actor
    /// passivation or idempotency-cache eviction) the same ID is evaluated afresh.
    /// </summary>
    Refused
}

/// <summary>
/// The decision outcome only. It never carries review content: deciding does not imply the read permission, and
/// callers read the exact review through <see cref="IHumanReviews.GetAsync"/>.
/// </summary>
public sealed record ReviewDecisionReceipt(ReviewDecisionReceiptStatus Status, string? Reason = null);
