using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Agent.Specialists.Review.Models;

/// <summary>Stable identities of the human review wait. They are persisted wire values.</summary>
public static class HumanReviewIds
{
    public const string ActivityType = "Elsa.Agent.Specialists.Review.Activities.HumanReview";
    public const string StimulusType = "elsa.agent.human-review.v1";
    public const string ResumeTargetKey = "resume-target:human-review";
    public const string ProviderId = "elsa.agent.human-review.v1";
    public const string ReadPermission = "agent-reviews:read";
    public const string DecidePermission = "agent-reviews:decide";
    public const int FormatVersion = 1;
    public const int MaximumContentBytes = 65_536;
    public const int MaximumTextCharacters = 4_096;
}

/// <summary>A permitted person's decision. It is separate from any model's own assessment of the content.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReviewDecision>))]
public enum ReviewDecision
{
    Approve,
    Deny
}

/// <summary>
/// The exact pinned wait a decision targets. Every identity field is bound into the decision envelope. The native
/// runtime persists it as private state with default serializer options; <c>HumanReviewRecords</c> reads it the same way.
/// </summary>
public sealed record HumanReviewState(
    int FormatVersion,
    string TenantId,
    string Partition,
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ExecutableNodeId,
    string ContentHash,
    string WaitId,
    JsonElement Content,
    string? Title,
    string? Notes);

/// <summary>The typed trigger. Its only content is the protected decision envelope.</summary>
public sealed record ReviewDecisionDelivery(int FormatVersion, string Envelope);

/// <summary>The terminal human decision about the exact content. Approval performs no external effect.</summary>
public sealed record HumanReviewResult(
    string Decision,
    string ContentHash,
    JsonElement Content,
    string? Title,
    string? Notes,
    string DecidedBy,
    string? Comment,
    DateTimeOffset DecidedAt,
    string RequestId);
