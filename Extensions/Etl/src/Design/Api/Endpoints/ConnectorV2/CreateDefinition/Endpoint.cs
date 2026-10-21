using Elsa.Etl.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ConnectorV2.CreateDefinition;

[Post("/etl/v2/definitions")]
[RequirePermission(EtlDesignPermissions.ManageDefinitions)]
[EtlBoundedBody]
public sealed class Endpoint(IEtlConnectorDesignOperations operations) : ApiEndpoint<EtlConnectorCreateDefinitionRequest, EtlConnectorDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlV2CreateDefinition";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.SuccessStatus = StatusCodes.Status201Created;
    }

    public override async Task<EtlConnectorDraftView> HandleAsync(EtlConnectorCreateDefinitionRequest request, CancellationToken cancellationToken)
    {
        var response = await operations.CreateAsync(request, cancellationToken);
        HttpContext.Response.Headers.Location = $"{HttpContext.Request.PathBase}/etl/v2/definitions/{Uri.EscapeDataString(response.DefinitionId)}";
        return response;
    }
}
