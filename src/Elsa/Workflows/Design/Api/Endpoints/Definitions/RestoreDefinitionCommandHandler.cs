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

public sealed class RestoreDefinitionCommandHandler(
    IWorkflowDefinitionStore definitionStore,
    ISaveWorkflowDefinitionCommand saveCommand)
    : ICommandHandler<RestoreDefinition>
{
    public async Task<Unit> Handle(RestoreDefinition command, CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetAsync(command.DefinitionId, cancellationToken);
        if (definition.DeletedAt is not null || definition.DeletedReason is not null)
        {
            definition.DeletedAt = null;
            definition.DeletedReason = null;
            await saveCommand.Execute(
                DesignOperationKey.CreateOrGenerate(command.OperationKey),
                definition,
                cancellationToken);
        }
        return Unit.Instance;
    }
}
