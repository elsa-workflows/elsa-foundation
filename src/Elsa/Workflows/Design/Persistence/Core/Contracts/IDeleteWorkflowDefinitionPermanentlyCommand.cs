using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

public interface IDeleteWorkflowDefinitionPermanentlyCommand
{
    Task Execute(
        DesignOperationKey operationKey,
        string definitionId,
        CancellationToken cancellationToken = default);
}
