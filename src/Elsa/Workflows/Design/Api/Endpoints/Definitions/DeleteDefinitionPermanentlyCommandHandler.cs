using Elsa.Events.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

public sealed class DeleteDefinitionPermanentlyCommandHandler(
    IDeleteWorkflowDefinitionPermanentlyCommand deleteCommand)
    : ICommandHandler<DeleteDefinitionPermanently>
{
    public async Task<Unit> Handle(DeleteDefinitionPermanently command, CancellationToken cancellationToken)
    {
        await deleteCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.DefinitionId,
            cancellationToken);
        return Unit.Instance;
    }
}
