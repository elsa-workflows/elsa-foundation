using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Api.FastEndpoints.Constants;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

/// <summary>Soft-deletes a definition without changing its authored dependents or Publishing state.</summary>
internal sealed class Delete(ICommandSender commandSender, ILogger<Delete> logger)
    : NoContentDesignCommandEndpoint<SoftDeleteDefinition>(commandSender, logger)
{
    public override void Configure()
    {
        Delete(RouteConstants.GetRoute("definitions/{definitionId}"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }
}
