using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

internal sealed class Restore(ICommandSender commandSender, ILogger<Restore> logger)
    : NoContentDesignCommandEndpoint<RestoreDefinition>(commandSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("definitions/{definitionId}/restore"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }
}
