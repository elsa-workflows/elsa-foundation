namespace Elsa.Activities.DispatchWorkflow.Runtime.Configuration;

/// <summary>Host policy for bounded cross-workflow dispatch nesting.</summary>
public sealed class DispatchWorkflowOptions
{
    public const int DefaultMaxNestingDepth = 32;

    /// <summary>
    /// Maximum permitted depth of a dispatched child. Root executions have depth zero and each
    /// <c>DispatchWorkflow</c> edge adds one.
    /// </summary>
    public int MaxNestingDepth { get; set; } = DefaultMaxNestingDepth;

    internal static void ValidateMaxNestingDepth(int value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "DispatchWorkflow maximum nesting depth must be positive.");
    }
}
