using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

internal sealed class Discard(ICommandSender commandSender, ILogger<Discard> logger)
    : NoContentDesignCommandEndpoint<DiscardDraft>(commandSender, logger)
{
    public override void Configure()
    {
        Delete(RouteConstants.GetRoute("drafts/{draftId}"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }
}
