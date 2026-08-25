using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;

namespace Elsa.Workflows.Runtime.Api.Endpoints.AlterationPlans.JobsPage;

[Get(AlterationRouteConstants.JobsPage)]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[AlterationProblems("handling runtime alteration request", "InvalidAlterationJobsPage", "The alteration jobs page request is invalid.", EntityNotFoundArm = true)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<PageWorkflowAlterationJobs, WorkflowAlterationJobPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "PageAlterationJobs";

    public override Task<WorkflowAlterationJobPageView> HandleAsync(PageWorkflowAlterationJobs request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
