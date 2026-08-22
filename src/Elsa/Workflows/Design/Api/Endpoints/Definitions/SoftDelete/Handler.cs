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

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.SoftDelete;

public sealed class Handler(
    IWorkflowDefinitionStore definitionStore,
    ISaveWorkflowDefinitionCommand saveCommand,
    TimeProvider timeProvider)
    : ICommandHandler<SoftDeleteDefinition>
{
    public async Task<Unit> Handle(SoftDeleteDefinition command, CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetAsync(command.DefinitionId, cancellationToken);
        if (definition.DeletedAt is null)
        {
            definition.DeletedAt = timeProvider.GetUtcNow();
            definition.DeletedReason = command.Reason;
            await saveCommand.Execute(
                DesignOperationKey.CreateOrGenerate(command.OperationKey),
                definition,
                cancellationToken);
        }
        return Unit.Instance;
    }
}
