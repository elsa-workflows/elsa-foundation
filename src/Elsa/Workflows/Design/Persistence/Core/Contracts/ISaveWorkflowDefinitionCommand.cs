using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

public interface ISaveWorkflowDefinitionCommand
{
    Task Execute(WorkflowDefinition definition, CancellationToken cancellationToken = default);
}
