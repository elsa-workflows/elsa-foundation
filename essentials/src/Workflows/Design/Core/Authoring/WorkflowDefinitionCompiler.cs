using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Authoring;

/// <summary>
/// Explicit publishing-facing entry point for compiling a code-first definition into ordinary authored state.
/// </summary>
public sealed class WorkflowDefinitionCompiler
{
    public WorkflowDefinitionState Compile<TRequest, TResult>(
        WorkflowDefinition<TRequest, TResult> definition,
        string definitionVersionId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Compile(definitionVersionId);
    }
}
