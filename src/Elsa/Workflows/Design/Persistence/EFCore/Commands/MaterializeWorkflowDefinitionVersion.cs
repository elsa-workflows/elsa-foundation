using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

/// <summary>
/// EF Core oracle for immutable source-authored workflow version materialization. The supplied
/// identity and semantic version are retained exactly as provided by the reconciliation source.
/// </summary>
public sealed class MaterializeWorkflowDefinitionVersion(IAddCommand<WorkflowDefinitionVersion> addCommand)
    : IMaterializeWorkflowDefinitionVersionCommand
{
    public async Task<WorkflowDefinitionVersionAdded> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinitionVersion version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(version);

        await addCommand.Add(version, cancellationToken);
        return new WorkflowDefinitionVersionAdded(version.DefinitionId, version.Id, version.Version);
    }
}
