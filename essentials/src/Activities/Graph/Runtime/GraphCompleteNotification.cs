namespace Elsa.Activities.Graph.Runtime;

/// <summary>The ancestor notification a <c>Complete</c> node sends to its nearest graph boundary.</summary>
internal static class GraphCompleteNotification
{
    /// <summary>The notification code the graph boundary handles.</summary>
    public const string Code = "elsa.graph.complete";

    /// <summary>The payload property that carries the boundary outcome reference key.</summary>
    public const string OutcomePayloadKey = "outcome";
}
