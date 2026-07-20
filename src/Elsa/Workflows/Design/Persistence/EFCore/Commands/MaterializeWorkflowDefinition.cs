using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

/// <summary>
/// EF Core oracle for source-owned workflow definition materialization. The operation key is not
/// recorded because the EF implementation is intentionally outside the idempotency-ledger path.
/// </summary>
public sealed class MaterializeWorkflowDefinition(IAddCommand<WorkflowDefinition> addCommand)
    : IMaterializeWorkflowDefinitionCommand
{
    public async Task<string> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(definition);

        await addCommand.Add(definition, cancellationToken);
        return definition.Id;
    }
}
