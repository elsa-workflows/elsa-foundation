using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.TestRuns.Stop;

[Post("/publishing/workflow-test-runs/{testRunId}/stop")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IWorkflowTestRunStarter testRuns)
    : ApiEndpoint<StopWorkflowTestRun, WorkflowTestRunView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "StopWorkflowTestRun";
        options.BodyMode = EndpointBodyMode.None;
        options.SuccessStatus = StatusCodes.Status202Accepted;
        options.DocumentedStatus = StatusCodes.Status200OK;
    }

    public override async Task<WorkflowTestRunView> HandleAsync(
        StopWorkflowTestRun request,
        CancellationToken cancellationToken) =>
        await testRuns.StopAsync(request.TestRunId, cancellationToken)
        ?? throw new Elsa.Primitives.Exceptions.EntityNotFoundException($"Workflow test run '{request.TestRunId}' was not found.");
}
