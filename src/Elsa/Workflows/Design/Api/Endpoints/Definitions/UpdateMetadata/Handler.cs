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
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Get;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.UpdateMetadata;

public sealed class Handler(
    IWorkflowDefinitionStore definitionStore,
    ISaveWorkflowDefinitionCommand saveCommand,
    IRequestSender requestSender)
    : ICommandHandler<UpdateDefinitionMetadata, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(UpdateDefinitionMetadata command, CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetAsync(command.DefinitionId, cancellationToken);
        if (command.Name is not null)
        {
            var name = command.Name.Trim();
            if (name.Length == 0)
                throw new ArgumentException("Workflow definition name cannot be empty.");
            definition.Name = name;
        }
        if (command.Description is { } description)
        {
            definition.Description = description.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => description.GetString(),
                _ => throw new ArgumentException("Workflow definition description must be a string or null.")
            };
        }
        await saveCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            definition,
            cancellationToken);
        return await requestSender.Send(new GetDefinition(command.DefinitionId), cancellationToken);
    }
}
