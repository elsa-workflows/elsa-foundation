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
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.Add;

[Post("/design/activities/definitions")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<CreateReusableActivityDefinitionRequest, ReusableActivityDefinitionMutationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsAdd";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.Convention(builder => builder.AddOpenApiOperationTransformer(ActivityNodePresentationOpenApi.ConfigureAsync<CreateReusableActivityDefinitionRequest>));
        options.SuccessStatus = StatusCodes.Status201Created;
    }

    public override async Task<ReusableActivityDefinitionMutationView> HandleAsync(CreateReusableActivityDefinitionRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateDefinitionAsync(request.ToCommand(), cancellationToken);
        HttpContext.Response.Headers.Location = $"/{RouteConstants.GetRoute($"definitions/{response.Definition.DefinitionId}")}";
        return response;
    }
}
