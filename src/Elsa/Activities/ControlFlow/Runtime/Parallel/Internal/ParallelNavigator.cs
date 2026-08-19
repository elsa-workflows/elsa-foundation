using Elsa.Activities.Parallel.Exceptions;
using Elsa.Activities.Parallel.Models;
using Elsa.Workflows.Runtime.Core.Models;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.Parallel.Internal;

/// <summary>
/// Resolves the executable branch nodes for a <c>Parallel</c> executable node by reading its compiled
/// structure and matching the recorded branch node ids against the named child slots. Mirrors
/// <c>SwitchNavigator</c>: structure is the branch ordering/identity source of truth and the child slots
/// carry the actual executable nodes. Also derives the effective join threshold (how many branches must
/// finish before the <c>Parallel</c> joins).
/// </summary>
internal sealed class ParallelNavigator
{
    private readonly IReadOnlyList<ParallelBranch> _branches;
    private readonly IReadOnlyDictionary<string, ParallelBranch> _branchesByNodeId;

    private ParallelNavigator(IReadOnlyList<ParallelBranch> branches, int? threshold)
    {
        _branches = branches;

        var branchesByNodeId = new Dictionary<string, ParallelBranch>(StringComparer.Ordinal);
        foreach (var branch in branches.Where(branch => branch.Node is not null))
        {
            if (!branchesByNodeId.TryAdd(branch.Node!.ExecutableNodeId, branch))
                throw new ParallelExecutionException($"Parallel structure references branch node '{branch.Node.ExecutableNodeId}' from more than one branch.");
        }

        _branchesByNodeId = branchesByNodeId;

        // The set of branches that actually schedule a child; empty branches never run so they cannot
        // contribute to the join count.
        var runnableCount = branches.Count(branch => branch.Node is not null);

        // A null/out-of-range threshold means "all runnable branches"; clamp a configured threshold into
        // 1..runnableCount so a misconfigured value can never deadlock the join. (A threshold >= runnableCount
        // collapses to "all", which is exactly the runnableCount fallback.)
        EffectiveThreshold = threshold is { } configured && configured >= 1 && configured <= runnableCount
            ? configured
            : runnableCount;
    }

    /// <summary>The runnable branch nodes (one per non-empty branch slot), in authored order.</summary>
    public IReadOnlyList<ExecutableNode> RunnableBranches =>
        _branches.Where(branch => branch.Node is not null).Select(branch => branch.Node!).ToArray();

    /// <summary>The number of branches that must finish before the <c>Parallel</c> joins (always at least 0).</summary>
    public int EffectiveThreshold { get; }

    public static ParallelNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        if (executableNode.ChildSlots.Count == 0 && executableNode.Structure is null)
            return new ParallelNavigator([], threshold: null);

        var structure = ExecutableStructureReader.ReadStructure<ParallelExecutableStructure>(
            executableNode, "Parallel", ParallelActivity.StructureKind, ParallelActivity.StructureSchemaVersion, Fail);

        var duplicateName = structure.Branches
            .GroupBy(branch => branch.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateName is not null)
            throw new ParallelExecutionException($"Parallel executable node '{executableNode.ExecutableNodeId}' structure contains duplicate branch '{duplicateName}'.");

        var branches = structure.Branches
            .Select(branch => new ParallelBranch(branch.Name, MatchBranch(
                executableNode,
                $"branch '{branch.Name}'",
                branch.Activity,
                ParallelActivity.BranchSlotName(branch.Name))))
            .ToArray();

        return new ParallelNavigator(branches, structure.Threshold);
    }

    /// <summary>True when the given executable node id is one of this Parallel's runnable branches.</summary>
    public bool IsBranch(string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        return _branchesByNodeId.ContainsKey(executableNodeId);
    }

    private static ExecutableNode? MatchBranch(
        ExecutableNode executableNode,
        string branchName,
        string? structureNodeId,
        string slotName)
    {
        var slotChild = ExecutableStructureReader.ResolveSingleSlotChild(executableNode, slotName, "Parallel", "branch", Fail);
        return ExecutableStructureReader.MatchSingleSlotChild(
            executableNode, "Parallel", slotName, "branch", $"{branchName} branch", structureNodeId, slotChild, Fail);
    }

    private static ParallelExecutionException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);

    private sealed record ParallelBranch(string Name, ExecutableNode? Node);
}

