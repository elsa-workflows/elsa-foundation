using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>The view returned by <c>POST runtime/workflows/stimuli</c>: which instances the stimulus started and resumed.</summary>
public sealed record DispatchStimulusResponse(
    int StartedCount,
    int SkippedStartCount,
    int ResumedCount,
    IReadOnlyCollection<StimulusStartView> Starts,
    IReadOnlyCollection<StimulusResumeView> Resumes,
    int ShedStartCount = 0,
    int? RetryAfterSeconds = null)
{
    public static DispatchStimulusResponse From(StimulusRoutingResult result) =>
        new(
            result.StartedCount,
            result.SkippedStartCount,
            result.ResumedCount,
            result.Starts.Select(StimulusStartView.From).ToArray(),
            result.Resumes.Select(StimulusResumeView.From).ToArray(),
            result.ShedStartCount,
            result.ShedRetryAfter is { } retryAfter ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)) : null);
}

/// <summary>One started, duplicate-skipped or admission-shed start produced by routing a stimulus to the trigger index.</summary>
public sealed record StimulusStartView(string TriggerBindingId, string ArtifactId, string Status, string? WorkflowExecutionId)
{
    public static StimulusStartView From(StimulusStartOutcome outcome) =>
        new(outcome.TriggerBindingId, outcome.ArtifactId, outcome.Status.ToString(), outcome.WorkflowExecutionId);
}

/// <summary>One waiting instance the stimulus fanned in to via the cross-execution bookmark index.</summary>
public sealed record StimulusResumeView(string WorkflowExecutionId, string Status, string? Reason)
{
    public static StimulusResumeView From(StimulusResumeOutcome outcome) =>
        new(outcome.WorkflowExecutionId, outcome.Status.ToString(), outcome.Reason);
}
