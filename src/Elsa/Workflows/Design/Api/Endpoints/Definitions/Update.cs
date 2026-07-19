using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Api.FastEndpoints.Constants;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

/// <summary>Updates definition metadata only; authored state is replaced through the first-class Draft endpoint.</summary>
internal sealed class Update(ICommandSender commandSender, ILogger<Update> logger)
    : WorkflowDefinitionCommandHandlerEndpoint<UpdateDefinitionMetadata, WorkflowDefinitionDetailsView>(commandSender, logger)
{
    public override void Configure()
    {
        Patch(RouteConstants.GetRoute("definitions/{definitionId}"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }
}
