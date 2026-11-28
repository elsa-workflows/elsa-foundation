using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Design.Models;

/// <summary>
/// Authored design-time structure for a <c>BulkDispatchWorkflows</c> node: the <c>Dispatch</c> node run once per item
/// and the optional <c>ChildCompleted</c> and <c>ChildFaulted</c> slot activities, each a single child.
/// </summary>
public sealed class BulkDispatchWorkflowsAuthoredStructure
{
    [JsonConstructor]
    public BulkDispatchWorkflowsAuthoredStructure(ActivityNode? dispatch = null, ActivityNode? childCompleted = null, ActivityNode? childFaulted = null)
    {
        Dispatch = dispatch;
        ChildCompleted = childCompleted;
        ChildFaulted = childFaulted;
    }

    [JsonPropertyName("dispatch")]
    public ActivityNode? Dispatch { get; }

    [JsonPropertyName("childCompleted")]
    public ActivityNode? ChildCompleted { get; }

    [JsonPropertyName("childFaulted")]
    public ActivityNode? ChildFaulted { get; }
}
