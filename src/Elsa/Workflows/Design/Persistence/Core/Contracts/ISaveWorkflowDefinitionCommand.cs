using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

public interface ISaveWorkflowDefinitionCommand
{
    Task Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default);
}
