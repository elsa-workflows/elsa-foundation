using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Attributes;

namespace Elsa.Agent.Specialists.Editorial.Models;

/// <summary>Stable identities of the finite editorial review example. They are persisted wire values.</summary>
public static class EditorialReviewIds
{
    public const string ActivityType = "Elsa.Agent.Specialists.Editorial.Activities.ReviewEditorialContent";
    public const string GateActivityType = "Elsa.Agent.Specialists.Editorial.Activities.EditorialRevisionGate";
    public const string StimulusType = "elsa.agent.editorial-review.v1";
    public const string ResumeTargetKey = "resume-target:editorial-review";
    public const string ProviderId = "elsa.agent.editorial-review.v1";
    public const string ReadPermission = "editorial-reviews:read";
    public const string DecidePermission = "editorial-reviews:decide";
    public const int FormatVersion = 1;
    public const int MaximumRounds = 3;
    public const int MaximumArticleCharacters = 65_536;
    public const int MaximumCommentCharacters = 4_096;
}

/// <summary>A permitted person's content decision. It is separate from the model's review verdict.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialDecision>))]
public enum EditorialDecision
{
    Approve,
    Deny
}

/// <summary>The exact pinned wait a decision targets. Every field is bound into the decision envelope.</summary>
public sealed record EditorialReviewState(
    int FormatVersion,
    string TenantId,
    string Partition,
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ExecutableNodeId,
    int Round,
    string ArticleHash,
    string WaitId,
    string Article,
    string? ReviewComments);

/// <summary>The typed trigger. Its only content is the protected decision envelope.</summary>
public sealed record EditorialDecisionDelivery(int FormatVersion, string Envelope);

/// <summary>The terminal human decision. Approval performs no external publication effect.</summary>
public sealed record EditorialReviewResult(
    string Decision,
    int Round,
    string ArticleHash,
    string Article,
    string? ReviewComments,
    string DecidedBy,
    string? Comment,
    DateTimeOffset DecidedAt,
    string RequestId);

/// <summary>
/// The gate's routing decision. Each member is a result projection so ordinary output captures can carry
/// it into workflow variables for the next writer call or the human review.
/// </summary>
public sealed record EditorialRevisionResult(
    [property: Output(Key = "Round", Path = "round")] int Round,
    [property: Output(Key = "Awaiting", Path = "awaiting")] string Awaiting,
    [property: Output(Key = "PreviousArticle", Path = "previousArticle", IsRequired = false)] string? PreviousArticle,
    [property: Output(Key = "Feedback", Path = "feedback", IsRequired = false)] string? Feedback,
    [property: Output(Key = "Article", Path = "article", IsRequired = false)] string? Article,
    [property: Output(Key = "Verdict", Path = "verdict", IsRequired = false)] string? Verdict);
