using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Folders;

internal sealed class List(IRequestSender requestSender, ILogger<List> logger)
    : ElsaRequestHandlerEndpoint<ListWorkflowFolders, WorkflowFolderListView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.Folders);
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }
}
