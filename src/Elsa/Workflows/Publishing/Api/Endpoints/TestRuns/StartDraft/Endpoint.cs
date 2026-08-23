using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Endpoints.TestRuns.StartDraft;

[Post("/publishing/workflows/drafts/test-runs")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<StartWorkflowDraftTestRun, WorkflowTestRunView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "TestRunsStartDraft";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<WorkflowTestRunView> HandleAsync(StartWorkflowDraftTestRun request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
