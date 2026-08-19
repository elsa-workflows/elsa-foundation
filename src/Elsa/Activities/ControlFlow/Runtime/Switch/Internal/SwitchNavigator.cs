using Elsa.Activities.Switch.Exceptions;
using Elsa.Activities.Switch.Models;
using Elsa.Workflows.Runtime.Core.Models;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Internal;

/// <summary>
/// Resolves the executable case / default branch nodes for a <c>Switch</c> executable node by reading its
/// compiled structure and matching the recorded branch node ids against the named child slots. Mirrors
/// <c>IfNavigator</c>: structure is the case ordering/identity source of truth and the child slots carry
/// the actual executable nodes.
/// </summary>
internal sealed class SwitchNavigator
{
    private readonly IReadOnlyList<SwitchCase> _cases;
    private readonly IReadOnlyDictionary<string, SwitchCase> _casesByNodeId;

    private SwitchNavigator(IReadOnlyList<SwitchCase> cases, ExecutableNode? @default)
    {
        _cases = cases;
        Default = @default;

        var casesByNodeId = new Dictionary<string, SwitchCase>(StringComparer.Ordinal);
        foreach (var @case in cases.Where(item => item.Branch is not null))
        {
            if (!casesByNodeId.TryAdd(@case.Branch!.ExecutableNodeId, @case))
                throw new SwitchExecutionException($"Switch structure references branch node '{@case.Branch.ExecutableNodeId}' from more than one case.");
        }

        _casesByNodeId = casesByNodeId;
    }

    public ExecutableNode? Default { get; }

    public static SwitchNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        if (executableNode.ChildSlots.Count == 0 && executableNode.Structure is null)
            return new SwitchNavigator([], null);

        var structure = ExecutableStructureReader.ReadStructure<SwitchExecutableStructure>(
            executableNode, "Switch", SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion, Fail);

        var duplicateMatch = SwitchCaseRules.DuplicateMatches(structure.Cases.Select(@case => @case.Match)).FirstOrDefault();
        if (duplicateMatch is not null)
            throw new SwitchExecutionException($"Switch executable node '{executableNode.ExecutableNodeId}' structure contains duplicate case '{duplicateMatch}'.");

        var cases = structure.Cases
            .Select(@case => new SwitchCase(@case.Match, MatchBranch(
                executableNode,
                $"case '{@case.Match}'",
                @case.Activity,
                SwitchActivity.CaseSlotName(@case.Match))))
            .ToArray();

        var @default = MatchBranch(
            executableNode,
            "default",
            structure.Default,
            SwitchActivity.DefaultSlotName);

        return new SwitchNavigator(cases, @default);
    }

    /// <summary>Selects the branch for the first case whose match value equals <paramref name="value"/>, or the default branch.</summary>
    public ExecutableNode? Select(string? value) => SelectCase(value).Branch;

    /// <summary>The outcome name the switch completes with for <paramref name="value"/>: the matched case's match value, or <c>Default</c>.</summary>
    public string OutcomeForValue(string? value) => SelectCase(value).Outcome;

    private (ExecutableNode? Branch, string Outcome) SelectCase(string? value)
    {
        var match = _cases.FirstOrDefault(@case => StringComparer.Ordinal.Equals(@case.Match, value));
        return match is not null
            ? (match.Branch, match.Match)
            : (Default, Workflows.Runtime.Core.Constants.ActivityOutcomes.Default);
    }

    /// <summary>Resolves the outcome name a completed (or selected) branch node belongs to: its case match value, or <c>Default</c>.</summary>
    public string OutcomeFor(string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);

        if (_casesByNodeId.TryGetValue(executableNodeId, out var @case))
            return @case.Match;

        if (Default is { } @default && StringComparer.Ordinal.Equals(@default.ExecutableNodeId, executableNodeId))
            return Workflows.Runtime.Core.Constants.ActivityOutcomes.Default;

        throw new SwitchExecutionException($"Completed child executable node '{executableNodeId}' is not a Switch branch.");
    }

    private static ExecutableNode? MatchBranch(
        ExecutableNode executableNode,
        string branchName,
        string? structureNodeId,
        string slotName)
    {
        var slotChild = ExecutableStructureReader.ResolveSingleSlotChild(executableNode, slotName, "Switch", "branch", Fail);
        return ExecutableStructureReader.MatchSingleSlotChild(
            executableNode, "Switch", slotName, "branch", $"{branchName} branch", structureNodeId, slotChild, Fail);
    }

    private static SwitchExecutionException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);

    private sealed record SwitchCase(string Match, ExecutableNode? Branch);
}

