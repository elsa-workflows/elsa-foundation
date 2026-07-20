using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Adds a new API-authored workflow version. Implementations allocate the semantic version and
/// durable identity so an exact retry can return the originally committed result.
/// </summary>
public interface IAddWorkflowDefinitionVersionCommand
{
    Task<WorkflowDefinitionVersionAdded> Execute(
        DesignOperationKey operationKey,
        string definitionId,
        WorkflowDefinitionState state,
        CancellationToken cancellationToken = default);
}
