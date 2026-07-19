using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints.TestRuns;

internal sealed class StartDraft(IRequestSender requestSender, ILogger<StartDraft> logger)
    : ElsaRequestHandlerEndpoint<StartWorkflowDraftTestRun, WorkflowTestRunView>(requestSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.WorkflowDraftTestRuns);
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }
}
