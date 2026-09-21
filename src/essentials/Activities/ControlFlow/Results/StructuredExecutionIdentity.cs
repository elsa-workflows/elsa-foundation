using System.Globalization;

namespace Elsa.Activities.ControlFlow.Results;

/// <summary>
/// Stable structural identities assigned to parallel branches and loop iterations. These identities,
/// rather than completion timestamps or scheduler arrival order, define structured result ordering.
/// </summary>
public static class StructuredExecutionIdentity
{
    public static string Branch(string parentActivityExecutionId, string branchExecutableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchExecutableNodeId);
        return $"{parentActivityExecutionId}:parallel-branch:{branchExecutableNodeId}";
    }

    public static string Iteration(string parentActivityExecutionId, int sourceIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceIndex);
        return $"{IterationPrefix(parentActivityExecutionId)}{sourceIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryReadIteration(string parentActivityExecutionId, string? iterationId, out int sourceIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        sourceIndex = 0;
        var prefix = IterationPrefix(parentActivityExecutionId);
        return iterationId is not null &&
               iterationId.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(iterationId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out sourceIndex) &&
               sourceIndex >= 0;
    }

    private static string IterationPrefix(string parentActivityExecutionId) =>
        $"{parentActivityExecutionId}:foreach-iteration:";
}
