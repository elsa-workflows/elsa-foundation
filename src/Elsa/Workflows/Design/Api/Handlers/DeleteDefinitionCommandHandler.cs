using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Persistence.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Handlers;

/// <summary>
/// Handles <see cref="DeleteDefinition"/> by forwarding to the permanent-delete lifecycle command, which
/// removes the definition together with its versions, current Draft, and layout.
/// </summary>
public sealed class DeleteDefinitionCommandHandler(IDeleteWorkflowDefinitionPermanentlyCommand deleteCommand)
    : ICommandHandler<DeleteDefinition>
{
    public async Task<Unit> Handle(DeleteDefinition command, CancellationToken cancellationToken)
    {
        await deleteCommand.Execute(
            new DesignOperationKey(command.OperationKey),
            command.Id,
            cancellationToken);
        return Unit.Instance;
    }
}
