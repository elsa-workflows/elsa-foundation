using Microsoft.AspNetCore.Builder;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Microsoft.AspNetCore.Http;
using MinimalEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.ConflictCopy;

[Post("/design/activities/drafts/{draftId}/conflict-copies")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<DraftConflictCopyRequest, ReusableActivityDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsConflictCopy";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.Convention(builder => builder.AddOpenApiOperationTransformer(ActivityNodePresentationOpenApi.ConfigureAsync<DraftConflictCopyRequest>));
        options.SuccessStatus = StatusCodes.Status201Created;
    }

    public override async Task<ReusableActivityDraftView> HandleAsync(DraftConflictCopyRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateConflictCopyAsync(request.ToCommand(), cancellationToken);
        HttpContext.Response.Headers.Location = $"/{RouteConstants.GetRoute($"drafts/{response.DraftId}")}";
        return response;
    }
}
