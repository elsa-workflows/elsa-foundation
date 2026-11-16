using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.ControlFlow.Results;

/// <summary>
/// Collects completed structured-child results in authored branch order or source iteration order,
/// independently of the order in which child completions reached the parent.
/// </summary>
public static class DeterministicResultCollector
{
    public static IReadOnlyList<ValueEnvelope> CollectBranches(
        IEnumerable<ActivityExecutionState> childExecutions,
        string parentActivityExecutionId,
        IReadOnlyList<string> branchExecutableNodeIds)
    {
        ArgumentNullException.ThrowIfNull(childExecutions);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        ArgumentNullException.ThrowIfNull(branchExecutableNodeIds);

        var duplicateNode = branchExecutableNodeIds
            .GroupBy(nodeId => nodeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateNode is not null)
            throw new ArgumentException($"Branch executable node '{duplicateNode}' appears more than once in authored order.", nameof(branchExecutableNodeIds));

        var children = DirectCompletedChildren(childExecutions, parentActivityExecutionId);
        return branchExecutableNodeIds
            .Select(nodeId => RequireSingle(
                children,
                state => StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, nodeId) &&
                         StringComparer.Ordinal.Equals(state.BranchId, StructuredExecutionIdentity.Branch(parentActivityExecutionId, nodeId)),
                $"branch '{nodeId}'"))
            .ToArray();
    }

    public static IReadOnlyList<ValueEnvelope> CollectIterations(
        IEnumerable<ActivityExecutionState> childExecutions,
        string parentActivityExecutionId,
        int expectedIterationCount)
    {
        ArgumentNullException.ThrowIfNull(childExecutions);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedIterationCount);

        var results = new ValueEnvelope?[expectedIterationCount];
        foreach (var state in DirectCompletedChildren(childExecutions, parentActivityExecutionId))
        {
            if (!StructuredExecutionIdentity.TryReadIteration(parentActivityExecutionId, state.IterationId, out var sourceIndex))
                throw new InvalidOperationException($"Completed loop child '{state.InvocationId}' carries invalid iteration identity '{state.IterationId}'.");
            if (sourceIndex >= expectedIterationCount)
                throw new InvalidOperationException($"Iteration identity '{state.IterationId}' exceeds the expected source collection size {expectedIterationCount}.");
            if (results[sourceIndex] is not null)
                throw new InvalidOperationException($"Iteration source index {sourceIndex} produced more than one completed result.");
            results[sourceIndex] = state.Completion!.Result;
        }

        for (var index = 0; index < results.Length; index++)
        {
            if (results[index] is null)
                throw new InvalidOperationException($"Iteration source index {index} did not produce a completed result.");
        }

        return results.Select(result => result!).ToArray();
    }

    private static ActivityExecutionState[] DirectCompletedChildren(
        IEnumerable<ActivityExecutionState> executions,
        string parentActivityExecutionId) =>
        executions
            .Where(state => StringComparer.Ordinal.Equals(state.ParentActivityExecutionId, parentActivityExecutionId) &&
                            state.Status == ActivityExecutionStatus.Completed &&
                            state.Completion is not null)
            .ToArray();

    private static ValueEnvelope RequireSingle(
        IReadOnlyCollection<ActivityExecutionState> states,
        Func<ActivityExecutionState, bool> predicate,
        string description)
    {
        var matches = states.Where(predicate).ToArray();
        return matches.Length switch
        {
            1 => matches[0].Completion!.Result,
            0 => throw new InvalidOperationException($"Structured {description} did not produce a completed result."),
            _ => throw new InvalidOperationException($"Structured {description} produced more than one completed result.")
        };
    }
}
