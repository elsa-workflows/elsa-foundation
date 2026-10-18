using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.TestRuns.Get;

[Get("/publishing/workflow-test-runs/{testRunId}")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IWorkflowTestRunStarter testRuns)
    : ApiEndpoint<GetWorkflowTestRun, WorkflowTestRunView>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "GetWorkflowTestRun";

    public override async Task<WorkflowTestRunView> HandleAsync(
        GetWorkflowTestRun request,
        CancellationToken cancellationToken) =>
        await testRuns.GetAsync(request.TestRunId, cancellationToken)
        ?? throw new EntityNotFoundException($"Workflow test run '{request.TestRunId}' was not found.");
}
