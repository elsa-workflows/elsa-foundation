using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A typed request an activity stages to publish a named-event stimulus durably-first (spec 135 D1). The staging
/// buffer turns each request into a <c>PublishStimulusIntentKind</c> post-commit intent that, after the activity's
/// own checkpoint commits, routes the stimulus in <see cref="StimulusRoutingMode.StartAndResume"/> mode.
/// </summary>
/// <remarks>
/// Deliberately <b>not</b> a raw <see cref="RuntimePostCommitIntent"/> (control-room ruling on spec 135 tripwire 1):
/// the request carries only the send's authored facets — the event name, an optional passive correlation id, and an
/// optional payload — so the buffer/consumer owns the intent kind and stimulus identity. An activity therefore cannot
/// forge an arbitrary post-commit intent kind through this seam (spoof resistance, the narrowest-seam / spec-123
/// precedent). The stimulus <c>(type, hash)</c> pair is derived by the buffer, never supplied here.
/// </remarks>
public sealed class PublishStimulusRequest
{
    public PublishStimulusRequest(
        string workflowExecutionId,
        string activityExecutionId,
        string eventName,
        string? correlationId = null,
        JsonElement? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        if (correlationId is not null && string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Correlation id cannot be blank when provided.", nameof(correlationId));

        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        EventName = eventName.Trim();
        CorrelationId = correlationId?.Trim();
        Payload = payload?.Clone();
    }

    /// <summary>The publishing activity's workflow execution id (the intent's owning execution).</summary>
    public string WorkflowExecutionId { get; }

    /// <summary>The publishing activity's invocation id — scopes the staging hand-off and the deterministic intent id.</summary>
    public string ActivityExecutionId { get; }

    /// <summary>The event name whose stimulus is published (drives <c>EventStimulus.Hash</c>).</summary>
    public string EventName { get; }

    /// <summary>An optional passive correlation id threaded into the dispatch request verbatim (spec 135 FR-5; broadcast until issue #1001).</summary>
    public string? CorrelationId { get; }

    /// <summary>An optional stimulus payload delivered to started/resumed instances as their input.</summary>
    public JsonElement? Payload { get; }
}
