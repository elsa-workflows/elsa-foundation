namespace Elsa.Workflows.Runtime.Core.Constants;

/// <summary>
/// Well-known <see cref="Elsa.Workflows.Runtime.Core.Models.ExecutableNode.Metadata"/> keys the publish
/// compiler stamps and the trigger extractor reads (W7, E3-1). Kept as a shared constant so the producer
/// (compiler) and consumer (extractor) cannot drift.
/// </summary>
public static class TriggerNodeMetadata
{
    /// <summary>
    /// Carries the authored activity's execution shape (<c>Action</c>/<c>Trigger</c>/<c>Job</c>/<c>Task</c>)
    /// projected onto the published node, so the trigger extractor can find start-trigger nodes without
    /// re-reading the design catalog.
    /// </summary>
    public const string ExecutionTypeKey = "executionType";

    /// <summary>The <see cref="ExecutionTypeKey"/> value that marks a node as a start-trigger.</summary>
    public const string TriggerExecutionType = "Trigger";
}
