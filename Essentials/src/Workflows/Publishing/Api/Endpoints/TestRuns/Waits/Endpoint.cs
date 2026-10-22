using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.TestRuns.Waits;

[Get("/publishing/workflow-test-runs/{testRunId}/waits")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(ITestRunWaitOperations waits) : ApiEndpoint<TestRunWaitsRequest, TestRunWaitsView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "TestRunWaits";
    public override Task<TestRunWaitsView> HandleAsync(TestRunWaitsRequest request, CancellationToken cancellationToken) => waits.GetAsync(request.TestRunId, cancellationToken);
}
