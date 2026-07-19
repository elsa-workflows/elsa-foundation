using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

internal sealed class Page(IRequestSender requestSender, ILogger<Page> logger)
    : ElsaRequestHandlerEndpoint<ListWorkflowDefinitionPage, WorkflowDefinitionPageView>(requestSender, logger)
{
    public override void Configure()
    {
        Get($"{RouteConstants.Definitions}/page");
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }
}
