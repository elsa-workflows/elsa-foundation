using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Materializes one immutable source-authored workflow definition version with its source-supplied
/// semantic version and identity.
/// </summary>
public interface IMaterializeWorkflowDefinitionVersionCommand
{
    Task<WorkflowDefinitionVersionAdded> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinitionVersion version,
        CancellationToken cancellationToken = default);
}
