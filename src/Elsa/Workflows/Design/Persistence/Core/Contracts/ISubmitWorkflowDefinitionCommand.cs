using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

public interface ISubmitWorkflowDefinitionCommand
{
    Task<SubmittedWorkflowDefinition> Execute(
        DesignOperationKey operationKey,
        string name,
        string? description,
        WorkflowDefinitionState state,
        CancellationToken cancellationToken = default);
}
