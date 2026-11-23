using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Plans how a draft's scripts follow a variable rename (spec 1222 FR-021, FR-022): the edits that keep each resolvable
/// literal read on the renamed variable, and the references to repair by hand. The workflow design validations feature
/// registers the default.
/// </summary>
/// <remarks>
/// Replacement contract: exactly one scoped implementation per host. A host's own registration made before the feature
/// replaces the default; a second registration fails the shell at start.
/// </remarks>
public interface IScriptVariableRenamePlanner
{
    /// <summary>
    /// Plans the rename of the variable <paramref name="referenceKey"/> declared by <paramref name="scopeId"/> to
    /// <paramref name="newName"/>. <paramref name="scriptInputKeyOf"/> names a node's script-body input, or null.
    /// </summary>
    ValueTask<ScriptVariableRenamePlan> PlanAsync(
        WorkflowDefinitionState state,
        string scopeId,
        string referenceKey,
        string newName,
        Func<ActivityNode, CancellationToken, ValueTask<string?>>? scriptInputKeyOf = null,
        CancellationToken cancellationToken = default);
}
