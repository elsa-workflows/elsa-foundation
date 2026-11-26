namespace Elsa.Activities.DispatchWorkflow.Runtime.Constants;

/// <summary>Stable outcomes exposed by <c>DispatchWorkflow</c> across its lifecycle slices.</summary>
public static class DispatchWorkflowOutcomes
{
    public const string Dispatched = "Dispatched";
    public const string Completed = "Completed";
    public const string Faulted = "Faulted";
    public const string Cancelled = "Cancelled";
    public const string DispatchFailed = "DispatchFailed";

    public static IReadOnlyList<string> All { get; } =
        [Dispatched, Completed, Faulted, Cancelled, DispatchFailed];
}
