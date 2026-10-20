using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.CreateDefinition;

[Post("/etl/definitions")]
[RequirePermission(EtlDesignPermissions.ManageDefinitions)]
[EtlBoundedBody]
public sealed class Endpoint(IEtlDesignOperations operations) : ApiEndpoint<EtlCreateDefinitionRequest, EtlDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlCreateDefinition";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.SuccessStatus = StatusCodes.Status201Created;
    }

    public override async Task<EtlDraftView> HandleAsync(EtlCreateDefinitionRequest request, CancellationToken cancellationToken)
    {
        var response = await operations.CreateAsync(request, cancellationToken);
        HttpContext.Response.Headers.Location = $"{HttpContext.Request.PathBase}/etl/definitions/{Uri.EscapeDataString(response.DefinitionId)}";
        return response;
    }
}
