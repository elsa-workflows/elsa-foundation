using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Folders;

internal sealed class Get(IRequestSender requestSender, ILogger<Get> logger)
    : ElsaRequestHandlerEndpoint<GetWorkflowFolder, WorkflowFolderDetailsView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("folders/{folderId}"));
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }
}
