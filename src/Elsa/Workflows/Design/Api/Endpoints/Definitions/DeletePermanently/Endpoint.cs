using Elsa.Api.AspNetCore;
using Elsa.Api.Mediator;
using Elsa.Events.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.DeletePermanently;

[Delete("definitions/{definitionId}/permanent")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(
    IDeleteWorkflowDefinitionPermanentlyCommand deleteCommand) : ApiEndpoint<DeleteDefinitionPermanently>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsDeletePermanently";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task HandleAsync(DeleteDefinitionPermanently command, CancellationToken cancellationToken)
    {
        await deleteCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.DefinitionId,
            cancellationToken);
    }
}
