using Microsoft.AspNetCore.Builder;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.Replace;

[Put("/design/activities/drafts/{draftId}")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<ReplaceReusableActivityDraftRequest, ReusableActivityDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsReplace";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.Convention(builder => builder.AddOpenApiOperationTransformer(ActivityNodePresentationOpenApi.ConfigureAsync<ReplaceReusableActivityDraftRequest>));
    }

    public override Task<ReusableActivityDraftView> HandleAsync(ReplaceReusableActivityDraftRequest request, CancellationToken cancellationToken) =>
        service.ReplaceDraftAsync(request.ToCommand(), cancellationToken);
}
