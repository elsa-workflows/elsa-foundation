namespace Elsa.Activities.Graph.Runtime;

/// <summary>The ancestor notification a <c>Complete</c> node sends to its nearest graph boundary.</summary>
internal static class GraphCompleteNotification
{
    /// <summary>The notification code the graph boundary handles.</summary>
    public const string Code = "elsa.graph.complete";

    /// <summary>The payload property that carries the boundary outcome reference key.</summary>
    public const string OutcomePayloadKey = "outcome";

    /// <summary>
    /// The payload key of every boundary outcome reference key a <c>Complete</c> node names, in order, when it names
    /// several (spec 1208 FR-032). <see cref="OutcomePayloadKey"/> then holds the first.
    /// </summary>
    public const string OutcomesPayloadKey = "outcomes";
}
