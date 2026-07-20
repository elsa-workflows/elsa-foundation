using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Materializes one source-owned workflow definition record during reconciliation without creating
/// an authored draft.
/// </summary>
public interface IMaterializeWorkflowDefinitionCommand
{
    Task<string> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default);
}
