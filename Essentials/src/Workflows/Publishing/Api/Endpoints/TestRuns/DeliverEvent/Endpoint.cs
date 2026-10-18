using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.TestRuns.DeliverEvent;

[Post("/publishing/workflow-test-runs/{testRunId}/events")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(ITestRunWaitOperations waits) : ApiEndpoint<TestRunEventRequest, TestRunEventView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "TestRunDeliverEvent";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }
    public override Task<TestRunEventView> HandleAsync(TestRunEventRequest request, CancellationToken cancellationToken) => waits.DeliverAsync(request, cancellationToken);
}
