using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// How a stimulus should be routed (W7). A stimulus can start new workflow instances (from the trigger
/// index), resume waiting instances (from the cross-execution bookmark index), or both.
/// </summary>
public enum StimulusRoutingMode
{
    /// <summary>Start new instances from matching triggers and resume matching waiting instances.</summary>
    StartAndResume,

    /// <summary>Only start new instances from matching triggers; do not resume waiting instances.</summary>
    StartOnly,

    /// <summary>Only resume matching waiting instances; do not start new instances.</summary>
    ResumeOnly
}

/// <summary>
/// A request to route an external stimulus to workflows (W7, E3-1 + E3-5). The stimulus identity is the
/// opaque <c>(StimulusType, StimulusHash)</c> pair that is already the engine's routing key on bookmarks
/// and trigger bindings; the router does not invent a second hashing scheme.
/// </summary>
public sealed class StimulusDispatchRequest
{
    public StimulusDispatchRequest(
        string stimulusType,
        string stimulusHash,
        JsonElement? input = null,
        string? correlationId = null,
        StimulusRoutingMode mode = StimulusRoutingMode.StartAndResume,
        string? idempotencyKey = null,
        string requestedBy = "runtime.stimulus",
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyCollection<WorkflowTriggerBinding>? matchedTriggerBindings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        if (correlationId is not null && string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Correlation id cannot be blank when provided.", nameof(correlationId));

        if (idempotencyKey is not null && string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be blank when provided.", nameof(idempotencyKey));

        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Input = input?.Clone();
        CorrelationId = correlationId;
        Mode = mode;
        IdempotencyKey = idempotencyKey;
        RequestedBy = requestedBy;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        MatchedTriggerBindings = matchedTriggerBindings;
    }

    public string StimulusType { get; }
    public string StimulusHash { get; }

    /// <summary>The JSON-encoded stimulus payload, delivered to resumed instances as their resume input. Optional.</summary>
    public JsonElement? Input { get; }

    /// <summary>A passive correlation id threaded into started/resumed instances (Condition B); it scopes the resume fan-in.</summary>
    public string? CorrelationId { get; }

    public StimulusRoutingMode Mode { get; }

    /// <summary>
    /// When present, the router deduplicates the START path: a duplicate delivery carrying the same key does not
    /// start a second instance. When absent, the start path is at-least-once and a duplicate delivery MAY
    /// double-start (see <c>docs/serialization.md</c> / the routing doc for the stated limits).
    /// </summary>
    public string? IdempotencyKey { get; }

    public string RequestedBy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// The matching start-trigger bindings, when the caller already fetched them for its own purposes (e.g. the
    /// HTTP endpoint middleware fetches the claimant set for its ambiguity guard and per-endpoint options). When
    /// supplied on a start-routing request the router reuses this set instead of issuing its own identical
    /// <c>ListByStimulusAsync(type, hash)</c> lookup — one durable read per request instead of two. Null means
    /// the router fetches the set itself (the default for callers that do not pre-resolve it). The set must be
    /// the complete match for <see cref="StimulusType"/>/<see cref="StimulusHash"/>; a partial set would silently
    /// under-start.
    /// </summary>
    public IReadOnlyCollection<WorkflowTriggerBinding>? MatchedTriggerBindings { get; }

    public IReadOnlyDictionary<string, string> BuildDispatchMetadata()
    {
        if (CorrelationId is null)
            return Metadata;

        var metadata = Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.CorrelationId] = CorrelationId;
        return metadata;
    }
}

/// <summary>The outcome of the router attempting to start one instance from one matched trigger binding.</summary>
public sealed class StimulusStartOutcome
{
    private StimulusStartOutcome(
        string triggerBindingId,
        string artifactId,
        StimulusStartStatus status,
        string? workflowExecutionId)
    {
        TriggerBindingId = triggerBindingId;
        ArtifactId = artifactId;
        Status = status;
        WorkflowExecutionId = workflowExecutionId;
    }

    public string TriggerBindingId { get; }
    public string ArtifactId { get; }
    public StimulusStartStatus Status { get; }

    /// <summary>The started instance's execution id; <c>null</c> when the start was skipped as a duplicate.</summary>
    public string? WorkflowExecutionId { get; }

    public static StimulusStartOutcome Started(string triggerBindingId, string artifactId, string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerBindingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        return new StimulusStartOutcome(triggerBindingId, artifactId, StimulusStartStatus.Started, workflowExecutionId);
    }

    public static StimulusStartOutcome SkippedDuplicate(string triggerBindingId, string artifactId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerBindingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        return new StimulusStartOutcome(triggerBindingId, artifactId, StimulusStartStatus.SkippedDuplicate, null);
    }
}

public enum StimulusStartStatus
{
    Started,
    SkippedDuplicate
}

/// <summary>The outcome of the router attempting to resume one waiting instance.</summary>
public sealed class StimulusResumeOutcome
{
    public StimulusResumeOutcome(string workflowExecutionId, BookmarkResumeDispatchStatus status, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        WorkflowExecutionId = workflowExecutionId;
        Status = status;
        Reason = reason;
    }

    public string WorkflowExecutionId { get; }
    public BookmarkResumeDispatchStatus Status { get; }
    public string? Reason { get; }
}

/// <summary>The aggregate result of routing a stimulus: which instances were started and which were resumed.</summary>
public sealed class StimulusRoutingResult
{
    public StimulusRoutingResult(IReadOnlyList<StimulusStartOutcome> starts, IReadOnlyList<StimulusResumeOutcome> resumes)
    {
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(resumes);
        Starts = starts;
        Resumes = resumes;
    }

    public IReadOnlyList<StimulusStartOutcome> Starts { get; }
    public IReadOnlyList<StimulusResumeOutcome> Resumes { get; }

    public int StartedCount => Starts.Count(start => start.Status == StimulusStartStatus.Started);
    public int SkippedStartCount => Starts.Count(start => start.Status == StimulusStartStatus.SkippedDuplicate);
    public int ResumedCount => Resumes.Count(resume => resume.Status == BookmarkResumeDispatchStatus.Dispatched);
}
