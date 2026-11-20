using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Reads the variables a script activity's body may write (spec 1231 FR-019, FR-020a, FR-036), resolved as publication
/// pins them: the variables it writes literally or lets an object value escape, or every visible variable when it writes
/// by a computed or opaque name. Studio shows it as the node's may-write set. The workflow design validations feature
/// registers the default.
/// </summary>
/// <remarks>
/// Replacement contract: exactly one scoped implementation per host. A host's own registration made before the feature
/// replaces the default; a second registration fails the shell at start.
/// </remarks>
public interface IScriptBodyWriteAnalyzer
{
    /// <summary>
    /// Analyzes the script body of node <paramref name="nodeId"/> in <paramref name="state"/>. <paramref name="scriptInputKeyOf"/>
    /// names a node's script-body input, or null. Returns null when the node has no script body authored as a literal.
    /// </summary>
    ValueTask<ScriptBodyWriteSet?> AnalyzeAsync(
        WorkflowDefinitionState state,
        string nodeId,
        Func<ActivityNode, CancellationToken, ValueTask<string?>> scriptInputKeyOf,
        CancellationToken cancellationToken = default);
}
