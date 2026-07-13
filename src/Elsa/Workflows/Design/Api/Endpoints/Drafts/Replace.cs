using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

internal sealed class Replace(ICommandSender commandSender, ILogger<Replace> logger)
    : ElsaCommandHandlerEndpoint<ReplaceDraft, WorkflowDraftView>(commandSender, logger)
{
    public override void Configure()
    {
        Put(RouteConstants.GetRoute("drafts/{draftId}"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }
}
