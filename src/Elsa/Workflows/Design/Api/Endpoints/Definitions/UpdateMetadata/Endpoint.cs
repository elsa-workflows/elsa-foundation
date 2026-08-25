using Elsa.Api.AspNetCore;
using Elsa.Events.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Get;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.UpdateMetadata;

[Patch("definitions/{definitionId}")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(
    IWorkflowDefinitionStore definitionStore,
    ISaveWorkflowDefinitionCommand saveCommand,
    IWorkflowDefinitionDetailsReader reader) : ApiEndpoint<UpdateDefinitionMetadata, WorkflowDefinitionDetailsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsUpdate";
        options.Accepts = ["application/json"];
    }

    public override async Task<WorkflowDefinitionDetailsView> HandleAsync(UpdateDefinitionMetadata command, CancellationToken cancellationToken)
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
        return await reader.ReadAsync(command.DefinitionId, cancellationToken);
    }
}
