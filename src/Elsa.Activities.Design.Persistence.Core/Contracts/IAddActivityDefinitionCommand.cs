using Elsa.Activities.Design.Persistence.Core.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Contracts;

public interface IAddActivityDefinitionCommand
{
    Task Execute(ActivityDefinition workflowDefinition, ActivityDefinitionVersion version, CancellationToken cancellation);
}
