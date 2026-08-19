using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>One scheduled, not-yet-completed Elsa child activity owned by a BPMN element.</summary>
public sealed record BpmnActiveChild
{
    [JsonConstructor]
    public BpmnActiveChild(
        string nodeId,
        string elementId,
        string tokenId,
        string schedulingCause,
        string? iterationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulingCause);

        NodeId = nodeId;
        ElementId = elementId;
        TokenId = tokenId;
        SchedulingCause = schedulingCause;
        IterationId = string.IsNullOrWhiteSpace(iterationId) ? null : iterationId;
    }

    /// <summary>The executable node id of the scheduled child activity.</summary>
    public string NodeId { get; init; }

    public string ElementId { get; init; }
    public string TokenId { get; init; }
    public string SchedulingCause { get; init; }

    /// <summary>
    /// The iteration id the child was scheduled with (spec 121 multi-instance instance children); <c>null</c>
    /// for ordinary single-run children. A teardown resolves this child's live activity-execution id from the
    /// <c>(NodeId, IterationId)</c> live-child map so N concurrent same-node instances resolve distinctly.
    /// </summary>
    public string? IterationId { get; init; }
}
