using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class MoveWorkflowDefinitionsCommandHandler(IMoveWorkflowDefinitionsCommand moveCommand)
    : ICommandHandler<MoveWorkflowDefinitions>
{
    public async Task<Unit> Handle(MoveWorkflowDefinitions command, CancellationToken cancellationToken)
    {
        if (command.DefinitionIds is null || command.DefinitionIds.Count == 0)
            throw new ArgumentException("At least one workflow definition must be selected.", nameof(command.DefinitionIds));
        if (command.DefinitionIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Workflow definition identifiers cannot be empty.", nameof(command.DefinitionIds));
        if (command.DefinitionIds.Distinct(StringComparer.Ordinal).Count() != command.DefinitionIds.Count)
            throw new ArgumentException("Workflow definition identifiers must be unique.", nameof(command.DefinitionIds));
        if (command.FolderId is not null)
            WorkflowFolderNames.ValidateIdentifier(command.FolderId, nameof(command.FolderId));

        await moveCommand.Execute(command.DefinitionIds, command.FolderId, cancellationToken);
        return Unit.Instance;
    }
}
