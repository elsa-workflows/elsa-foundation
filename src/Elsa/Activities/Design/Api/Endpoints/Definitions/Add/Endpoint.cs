using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.Add;

[Post("/design/activities/definitions")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<CreateReusableActivityDefinition, ReusableActivityDefinitionMutationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsAdd";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.SuccessStatus = StatusCodes.Status201Created;
    }

    public override async Task<ReusableActivityDefinitionMutationView> HandleAsync(CreateReusableActivityDefinition command, CancellationToken cancellationToken)
    {
        var response = await service.CreateDefinitionAsync(command, cancellationToken);
        HttpContext.Response.Headers.Location = $"/{RouteConstants.GetRoute($"definitions/{response.Definition.DefinitionId}")}";
        return response;
    }
}
