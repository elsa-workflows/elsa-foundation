using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.TestRuns.Options;

[Post("/publishing/workflows/drafts/test-run-options")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IWorkflowTestRunStarter testRuns)
    : ApiEndpoint<DraftTestRunOptionsRequest, DraftTestRunOptionsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "TestRunsDraftOptions";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<DraftTestRunOptionsView> HandleAsync(
        DraftTestRunOptionsRequest request,
        CancellationToken cancellationToken) =>
        testRuns.GetDraftOptionsAsync(request, cancellationToken);
}
