using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;

namespace Elsa.Workflows.Runtime.Api.Endpoints.AlterationPlans.Job;

[Get(AlterationRouteConstants.Job)]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[AlterationProblems("handling runtime alteration request", "InvalidAlterationJobId", "The alteration job identifier is invalid.", EntityNotFoundArm = true)]
public sealed class Endpoint(IWorkflowAlterationPlanApiService alterations) : ApiEndpoint<GetWorkflowAlterationJob, WorkflowAlterationJobView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetAlterationJob";

    public override Task<WorkflowAlterationJobView> HandleAsync(GetWorkflowAlterationJob request, CancellationToken cancellationToken) =>
        alterations.GetJobAsync(request, cancellationToken);
}
