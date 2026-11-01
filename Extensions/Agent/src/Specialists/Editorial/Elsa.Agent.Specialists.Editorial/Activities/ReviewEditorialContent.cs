using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Agent.Specialists.Editorial.Services;
using Elsa.Secrets.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Agent.Specialists.Editorial.Activities;

/// <summary>
/// Suspends one exact article for a permitted person's content decision. The wait is an ordinary native
/// bookmark; a decision reaches it only through the editorial decision endpoint, whose envelope the
/// pre-claim resume guard validates. Deny takes the Rejected branch and never publishes the article.
/// </summary>
[Activity(Icon = "UserCheck")]
[ActivityOutcome("Approved"), ActivityOutcome("Rejected")]
public sealed class ReviewEditorialContent(IWorkflowExecutionStateStore workflows, IPersistenceAccessContextAccessor access,
    ISecretValueProtector protector) : StatefulActivity<EditorialReviewResult, EditorialReviewState, EditorialDecisionDelivery>
{
    /// <summary>The exact article awaiting a decision.</summary>
    [ActivityInput(Key = nameof(Article), Order = 0)]
    [Required]
    public string Article { get; set; } = string.Empty;

    /// <summary>The model review round that approved the article, from one to three.</summary>
    [ActivityInput(Key = nameof(Round), Order = 1, DefaultValue = "1", DefaultSyntax = "Literal")]
    public int Round { get; set; } = 1;

    /// <summary>The model reviewer's comments, shown beside the article.</summary>
    [ActivityInput(Key = nameof(ReviewComments), Order = 2)]
    public string? ReviewComments { get; set; }

    protected override async ValueTask<ActivityTransition<EditorialReviewResult, EditorialReviewState>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(Article) || Article.Length > EditorialReviewIds.MaximumArticleCharacters ||
            Round is < 1 or > EditorialReviewIds.MaximumRounds || ReviewComments?.Length > EditorialReviewIds.MaximumCommentCharacters)
            return Fault(new("editorial.review.input-invalid", "The article, round or review comments are outside the editorial review bounds."));
        var parent = await workflows.FindAsync(context.WorkflowExecutionId, context.CancellationToken)
            ?? throw EditorialReviewException.Invalid("The review's workflow execution is missing.");
        var scope = access.Current.RequireScope();
        if (parent.WorkflowExecutionId != context.WorkflowExecutionId || string.IsNullOrWhiteSpace(parent.TenantId) ||
            parent.Partition?.Value is not { } partition || partition != scope.Value)
            return Fault(new("editorial.review.scope-invalid", "An editorial review requires a tenant-owned, partitioned workflow execution."));
        var articleHash = EditorialDecisionCodec.HashArticle(Article);
        var waitId = EditorialDecisionCodec.WaitId(context.WorkflowExecutionId, context.InvocationId, Round, articleHash);
        var state = new EditorialReviewState(EditorialReviewIds.FormatVersion, parent.TenantId, partition, context.WorkflowExecutionId,
            context.InvocationId, context.ExecutableNodeId, Round, articleHash, waitId, Article, ReviewComments);
        return Suspend(state, [Registration(waitId)]);
    }

    [ResumeTarget(EditorialReviewIds.ResumeTargetKey)]
    protected override ValueTask<ActivityTransition<EditorialReviewResult, EditorialReviewState>> ResumeAsync(
        ActivityResumeContext<EditorialReviewState, EditorialDecisionDelivery> context)
    {
        // The pre-claim guard validated the exact bookmark, expiry and current permission before the wait was
        // claimed. The activity re-checks that the authentic envelope targets this pinned wait identity.
        var state = context.State;
        if (context.Trigger.FormatVersion != EditorialReviewIds.FormatVersion || state.FormatVersion != EditorialReviewIds.FormatVersion ||
            context.WorkflowExecutionId != state.WorkflowExecutionId || context.InvocationId != state.ActivityExecutionId ||
            context.ExecutableNodeId != state.ExecutableNodeId)
            throw EditorialReviewException.Invalid("The decision does not belong to this review.");
        var claims = new EditorialDecisionCodec(protector).Read(context.Trigger.Envelope);
        if (!claims.Targets(state, bookmarkId: null) || EditorialDecisionCodec.HashArticle(state.Article) != state.ArticleHash)
            throw EditorialReviewException.Invalid("The decision does not target this exact article and wait.");
        var result = new EditorialReviewResult(claims.Decision.ToString(), state.Round, state.ArticleHash, state.Article,
            state.ReviewComments, claims.ActorId, claims.Comment, claims.IssuedAt, claims.RequestId);
        return ValueTask.FromResult(Complete(result, claims.Decision == EditorialDecision.Approve ? "Approved" : "Rejected"));
    }

    internal static ActivityTriggerRegistration<EditorialDecisionDelivery> Registration(string waitId) =>
        new(EditorialReviewIds.ResumeTargetKey, EditorialReviewIds.StimulusType, waitId, ActivityTriggerDeduplicationMode.Once);
}
