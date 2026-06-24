namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

public interface IDeleteWorkflowDefinitionPermanentlyCommand
{
    Task Execute(string definitionId, CancellationToken cancellationToken = default);
}
