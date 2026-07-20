using Elsa.Persistence.Core.Design;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

public interface IDeleteWorkflowDefinitionPermanentlyCommand
{
    Task Execute(
        DesignOperationKey operationKey,
        string definitionId,
        CancellationToken cancellationToken = default);
}
