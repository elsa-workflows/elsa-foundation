using System.Text.Json.Serialization;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>
/// Compiled executable structure for a <c>BulkDispatchWorkflows</c> node: the executable node ids of its
/// <c>Dispatch</c> node and its optional <c>ChildCompleted</c> and <c>ChildFaulted</c> slot children. The activities
/// themselves travel in the named child slots; this records which slot child plays which part.
/// </summary>
public sealed class BulkDispatchWorkflowsExecutableStructure
{
    [JsonConstructor]
    public BulkDispatchWorkflowsExecutableStructure(string? dispatch = null, string? childCompleted = null, string? childFaulted = null)
    {
        Dispatch = dispatch;
        ChildCompleted = childCompleted;
        ChildFaulted = childFaulted;
    }

    [JsonPropertyName("dispatch")]
    public string? Dispatch { get; }

    [JsonPropertyName("childCompleted")]
    public string? ChildCompleted { get; }

    [JsonPropertyName("childFaulted")]
    public string? ChildFaulted { get; }
}
