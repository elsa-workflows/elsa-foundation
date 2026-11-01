using Elsa.Agent.Specialists.Editorial.Models;

namespace Elsa.Agent.Specialists.Editorial.Contracts;

/// <summary>
/// Reads and decides one exact editorial review wait. Callers supply the server-derived tenant and actor;
/// nothing in a command confers authority. This is content approval only and grants no tool or effect permission.
/// </summary>
public interface IEditorialReviews
{
    ValueTask<EditorialReviewView?> GetAsync(EditorialReviewReference reference, EditorialDecider caller, CancellationToken cancellationToken = default);

    ValueTask<EditorialDecisionReceipt> DecideAsync(EditorialDecisionCommand command, EditorialDecider decider, CancellationToken cancellationToken = default);
}

public sealed record EditorialReviewReference(string WorkflowExecutionId, string ActivityExecutionId);

/// <summary>The authenticated caller, derived by the host from its normalized principal.</summary>
public sealed record EditorialDecider(string TenantId, string ActorId);

public sealed record EditorialDecisionCommand(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    int Round,
    string ArticleHash,
    EditorialDecision Decision,
    string? Comment,
    string RequestId);

public enum EditorialReviewStatus
{
    Pending,
    Approved,
    Rejected,
    Closed
}

public sealed record EditorialReviewView(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    int Round,
    string ArticleHash,
    string Article,
    string? ReviewComments,
    EditorialReviewStatus Status,
    string? DecidedBy,
    string? DecisionComment,
    string? RequestId);

public enum EditorialDecisionReceiptStatus
{
    /// <summary>This request's decision was admitted; repeating it replays this receipt.</summary>
    Accepted,

    /// <summary>The decision is queued for native admission; read the review to see it resolve.</summary>
    Pending,

    /// <summary>Another decision was already admitted for this wait.</summary>
    AlreadyResolved,

    /// <summary>The article/round changed, or this request ID already carries different content.</summary>
    Conflict,

    /// <summary>The caller does not currently hold the decide permission.</summary>
    Forbidden,

    /// <summary>No visible pending review exists for this caller.</summary>
    NotFound,

    /// <summary>The review ended without a decision, for example through cancellation.</summary>
    Closed,

    /// <summary>A dependency was temporarily unavailable; retry with the same request ID.</summary>
    Unavailable,

    /// <summary>Native admission refused this request (for example expiry or revoked permission); the wait stays open.</summary>
    Refused
}

/// <summary>
/// The decision outcome only. It never carries review content: deciding does not imply the read permission, and
/// callers read the exact review through <see cref="IEditorialReviews.GetAsync"/>.
/// </summary>
public sealed record EditorialDecisionReceipt(EditorialDecisionReceiptStatus Status, string? Reason = null);
