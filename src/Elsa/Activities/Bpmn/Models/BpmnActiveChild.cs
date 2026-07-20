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
        string schedulingCause)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulingCause);

        NodeId = nodeId;
        ElementId = elementId;
        TokenId = tokenId;
        SchedulingCause = schedulingCause;
    }

    /// <summary>The executable node id of the scheduled child activity.</summary>
    public string NodeId { get; init; }

    public string ElementId { get; init; }
    public string TokenId { get; init; }
    public string SchedulingCause { get; init; }
}
