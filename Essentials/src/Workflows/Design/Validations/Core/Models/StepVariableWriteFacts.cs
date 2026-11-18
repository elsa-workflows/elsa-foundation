namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>
/// The variables nodes can change in their step bodies (spec 1231 FR-019, FR-024, FR-028): the targets of a node's
/// writable variable-reference inputs, and whether it may write every visible variable (an activity type marked
/// <c>[AccessesUndeclaredVariables]</c>, or a script with a computed or opaque write). The concurrent-write rule counts
/// these writers next to captures and intrinsics.
/// </summary>
/// <param name="ByNodeId">The step writers by node id; a node that writes nothing in its step is absent.</param>
/// <param name="WorkflowVariableKeys">The workflow variables, visible everywhere, that a node writing every visible variable touches.</param>
public sealed record StepVariableWriteFacts(
    IReadOnlyDictionary<string, StepVariableWriters> ByNodeId,
    IReadOnlyCollection<string> WorkflowVariableKeys)
{
    public static StepVariableWriteFacts None { get; } = new(new Dictionary<string, StepVariableWriters>(StringComparer.Ordinal), []);
}

/// <summary>The may-write set of one node: its explicit targets and whether it may write every visible variable.</summary>
public sealed record StepVariableWriters(IReadOnlyCollection<StepVariableTarget> Targets, bool MayWriteEveryVisibleVariable);

/// <summary>A variable a step may write: its declaring scope (<c>workflow</c> for the root) and reference key.</summary>
public sealed record StepVariableTarget(string ScopeId, string ReferenceKey);
