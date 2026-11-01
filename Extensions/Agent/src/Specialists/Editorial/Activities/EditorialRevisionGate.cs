using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Editorial.Models;

namespace Elsa.Agent.Specialists.Editorial.Activities;

/// <summary>
/// The deterministic routing point of the bounded editorial revision. Ordinary control flow owns the loop:
/// the gate reads the previous stage from workflow variables and selects an outcome; no model prose, hidden
/// session or specialist runtime chooses the branch. One gate precedes every specialist call, so the shared
/// original deadline is checked before each call and is never reset per rewrite. At most three rounds run,
/// including the first: two rewrites and six specialist calls. A third rejection takes Exhausted, never
/// approval, and a reviewer verdict outside the declared enum faults before routing.
/// </summary>
[Activity(Icon = "GitBranch")]
[ActivityOutcome("Write"), ActivityOutcome("Review"), ActivityOutcome("Approved"), ActivityOutcome("Exhausted")]
public sealed class EditorialRevisionGate(TimeProvider time) : Activity<EditorialRevisionResult>
{
    public const string AwaitingArticle = "article";
    public const string AwaitingReview = "review";
    public const string AwaitingNothing = "none";
    public const string Approved = "approved";
    public const string ChangesRequested = "changes_requested";

    /// <summary>The stage the previous gate scheduled; empty on entry.</summary>
    [ActivityInput(Key = nameof(Awaiting), Order = 0)]
    public string? Awaiting { get; set; }

    /// <summary>The current round; zero on entry.</summary>
    [ActivityInput(Key = nameof(Round), Order = 1, DefaultValue = "0", DefaultSyntax = "Literal")]
    public int Round { get; set; }

    /// <summary>The latest committed Copywriter article.</summary>
    [ActivityInput(Key = nameof(Article), Order = 2)]
    public string? Article { get; set; }

    /// <summary>The latest committed Proofreader verdict.</summary>
    [ActivityInput(Key = nameof(Verdict), Order = 3)]
    public string? Verdict { get; set; }

    /// <summary>The latest committed Proofreader comments.</summary>
    [ActivityInput(Key = nameof(ReviewComments), Order = 4)]
    public string? ReviewComments { get; set; }

    /// <summary>Total rounds including the first. At most three.</summary>
    [ActivityInput(Key = nameof(MaxRounds), Order = 5, DefaultValue = "3", DefaultSyntax = "Literal")]
    public int MaxRounds { get; set; } = EditorialReviewIds.MaximumRounds;

    /// <summary>The original shared deadline for every specialist call of this revision.</summary>
    [ActivityInput(Key = nameof(Deadline), Order = 6)]
    public DateTimeOffset? Deadline { get; set; }

    protected override ValueTask<ActivityTransition<EditorialRevisionResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (MaxRounds is < 1 or > EditorialReviewIds.MaximumRounds)
            return Result(Fault("editorial.revision.bounds-invalid", "An editorial revision runs between one and three rounds."));
        return Result((Awaiting ?? string.Empty) switch
        {
            "" when Round == 0 => Call(new(1, AwaitingArticle, null, null, null, null), "Write"),
            AwaitingArticle when Round >= 1 && Round <= MaxRounds && !string.IsNullOrWhiteSpace(Article) =>
                Call(new(Round, AwaitingReview, null, null, Article, null), "Review"),
            AwaitingReview when Round >= 1 && Round <= MaxRounds && !string.IsNullOrWhiteSpace(Article) => Route(),
            _ => Fault("editorial.revision.state-invalid", "The editorial revision stage does not match its committed variables.")
        });
    }

    private ActivityTransition<EditorialRevisionResult> Route() => Verdict switch
    {
        Approved => ActivityTransition.Complete(new EditorialRevisionResult(Round, AwaitingNothing, null, ReviewComments, Article, Verdict), "Approved"),
        ChangesRequested when Round >= MaxRounds =>
            ActivityTransition.Complete(new EditorialRevisionResult(Round, AwaitingNothing, null, ReviewComments, Article, Verdict), "Exhausted"),
        ChangesRequested => Call(new(Round + 1, AwaitingArticle, Article, ReviewComments, Article, Verdict), "Write"),
        _ => Fault("editorial.review.verdict-invalid", "The reviewer verdict is not approved or changes_requested.")
    };

    private ActivityTransition<EditorialRevisionResult> Call(EditorialRevisionResult next, string outcome) =>
        Deadline is { } deadline && time.GetUtcNow() >= deadline
            ? Fault("editorial.revision.deadline-exceeded", "The editorial revision's shared deadline passed before the next specialist call.")
            : ActivityTransition.Complete(next, outcome);

    private static ActivityTransition<EditorialRevisionResult> Fault(string code, string message) =>
        ActivityTransition.Fault<EditorialRevisionResult>(new(code, message));

    private static ValueTask<ActivityTransition<EditorialRevisionResult>> Result(ActivityTransition<EditorialRevisionResult> transition) =>
        ValueTask.FromResult(transition);
}
