using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

internal sealed class DeletePermanently(ICommandSender commandSender, ILogger<DeletePermanently> logger)
    : NoContentDesignCommandEndpoint<DeleteDefinitionPermanently>(commandSender, logger)
{
    public override void Configure()
    {
        Delete(RouteConstants.GetRoute("definitions/{definitionId}/permanent"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }
}
