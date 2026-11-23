namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>The result of <see cref="Contracts.IUnorderedVariableWriteAnalyzer.Analyze"/>.</summary>
public sealed record UnorderedVariableWriteAnalysis(
    IReadOnlyList<UnorderedVariableWriteFinding> UnorderedWrites,
    IReadOnlyList<DuplicateCaptureTargetFinding> DuplicateCaptureTargets);

/// <summary>
/// One variable whose writes the definition does not order: every writer, the writers that can run more than once at
/// the same time, the structures that declared nothing, and the author-facing message.
/// </summary>
/// <param name="ScopeId">The declaring scope; the root scope is <c>workflow</c>.</param>
public sealed record UnorderedVariableWriteFinding(
    string ScopeId,
    string ReferenceKey,
    IReadOnlyList<string> WriterNodeIds,
    IReadOnlyList<string> SelfOverlappingNodeIds,
    IReadOnlyList<UndeclaredStructure> UndeclaredStructures,
    string Message);

/// <summary>A structure that did not declare what the analysis needed, so its children were treated as unordered.</summary>
public sealed record UndeclaredStructure(string NodeId, string Kind, UndeclaredStructureFact Fact);

/// <summary>Which declaration a structure left out.</summary>
public enum UndeclaredStructureFact
{
    /// <summary>How two of its children relate in time (<c>RelateChildren</c>).</summary>
    ChildOrder,

    /// <summary>Whether it completes only after its children (<c>CompletesAfterItsChildren</c>).</summary>
    Completion
}

/// <summary>Two or more outputs of one node capture into the same variable.</summary>
public sealed record DuplicateCaptureTargetFinding(
    string NodeId,
    string ScopeId,
    string ReferenceKey,
    IReadOnlyList<string> OutputKeys,
    string Message);
