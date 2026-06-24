using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints.TestRuns;

internal sealed class Start(IRequestSender requestSender, ILogger<Start> logger)
    : ElsaRequestHandlerEndpoint<StartWorkflowTestRun, WorkflowTestRunView>(requestSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("workflows/{versionId}/test-runs"));
        ConfigurePermissions();
    }
}
