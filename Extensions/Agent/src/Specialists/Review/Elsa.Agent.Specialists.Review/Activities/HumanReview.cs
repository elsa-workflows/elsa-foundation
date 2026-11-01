using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Review.Models;
using Elsa.Agent.Specialists.Review.Services;
using Elsa.Secrets.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Agent.Specialists.Review.Activities;

/// <summary>
/// Suspends exact content, typically a user-defined agent's output, for a permitted person's decision. The wait is an
/// ordinary native bookmark; a decision reaches it only through the review decision service, whose envelope the
/// pre-claim resume guard validates. Deny takes the Rejected branch and performs no effect. Loops and rewrites
/// around the review stay ordinary workflow control flow.
/// </summary>
[Activity(Icon = "UserCheck")]
[ActivityOutcome("Approved"), ActivityOutcome("Rejected")]
public sealed class HumanReview(IWorkflowExecutionStateStore workflows, IPersistenceAccessContextAccessor access,
    ISecretValueProtector protector) : StatefulActivity<HumanReviewResult, HumanReviewState, ReviewDecisionDelivery>
{
    /// <summary>The exact content awaiting a decision: any JSON value, such as text or a structured agent result.</summary>
    [ActivityInput(Key = nameof(Content), Order = 0)]
    [Required]
    public JsonElement Content { get; set; }

    /// <summary>A short heading shown to the reviewer.</summary>
    [ActivityInput(Key = nameof(Title), Order = 1)]
    public string? Title { get; set; }

    /// <summary>Context shown beside the content, such as a reviewing agent's comments.</summary>
    [ActivityInput(Key = nameof(Notes), Order = 2)]
    public string? Notes { get; set; }

    protected override async ValueTask<ActivityTransition<HumanReviewResult, HumanReviewState>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (Content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || Content.GetRawText().Length > HumanReviewIds.MaximumContentBytes ||
            Title?.Length > HumanReviewIds.MaximumTextCharacters || Notes?.Length > HumanReviewIds.MaximumTextCharacters)
            return Fault(new("agent.review.input-invalid", "The content, title or notes are outside the human review bounds."));
        var parent = await workflows.FindAsync(context.WorkflowExecutionId, context.CancellationToken);
        var scope = access.Current.RequireScope();
        if (parent is null || parent.WorkflowExecutionId != context.WorkflowExecutionId || string.IsNullOrWhiteSpace(parent.TenantId) ||
            parent.Partition?.Value is not { } partition || partition != scope.Value)
            return Fault(new("agent.review.scope-invalid", "A human review requires a tenant-owned, partitioned workflow execution."));
        var content = Content.Clone();
        var contentHash = ReviewDecisionCodec.HashContent(content);
        var waitId = ReviewDecisionCodec.WaitId(context.WorkflowExecutionId, context.InvocationId, contentHash);
        var state = new HumanReviewState(HumanReviewIds.FormatVersion, parent.TenantId, partition, context.WorkflowExecutionId,
            context.InvocationId, context.ExecutableNodeId, contentHash, waitId, content, Title, Notes);
        return Suspend(state, [Registration(waitId)]);
    }

    [ResumeTarget(HumanReviewIds.ResumeTargetKey)]
    protected override ValueTask<ActivityTransition<HumanReviewResult, HumanReviewState>> ResumeAsync(
        ActivityResumeContext<HumanReviewState, ReviewDecisionDelivery> context)
    {
        // The pre-claim guard validated the exact bookmark, expiry and current permission before the wait was
        // claimed. The activity re-checks that the authentic envelope targets this pinned wait identity.
        var state = context.State;
        if (context.Trigger.FormatVersion != HumanReviewIds.FormatVersion || state.FormatVersion != HumanReviewIds.FormatVersion ||
            context.WorkflowExecutionId != state.WorkflowExecutionId || context.InvocationId != state.ActivityExecutionId ||
            context.ExecutableNodeId != state.ExecutableNodeId)
            throw HumanReviewException.Invalid("The decision does not belong to this review.");
        var claims = new ReviewDecisionCodec(protector).Read(context.Trigger.Envelope);
        if (!claims.Targets(state, bookmarkId: null) || ReviewDecisionCodec.HashContent(state.Content) != state.ContentHash)
            throw HumanReviewException.Invalid("The decision does not target this exact content and wait.");
        var result = new HumanReviewResult(claims.Decision.ToString(), state.ContentHash, state.Content, state.Title, state.Notes,
            claims.ActorId, claims.Comment, claims.IssuedAt, claims.RequestId);
        return ValueTask.FromResult(Complete(result, claims.Decision == ReviewDecision.Approve ? "Approved" : "Rejected"));
    }

    internal static ActivityTriggerRegistration<ReviewDecisionDelivery> Registration(string waitId) =>
        new(HumanReviewIds.ResumeTargetKey, HumanReviewIds.StimulusType, waitId, ActivityTriggerDeduplicationMode.Once);
}
