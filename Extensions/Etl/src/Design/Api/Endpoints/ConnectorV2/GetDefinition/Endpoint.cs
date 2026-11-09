using Elsa.Etl.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ConnectorV2.GetDefinition;

[Get("/etl/v2/definitions/{id}")]
[RequirePermission(EtlDesignPermissions.ReadDefinitions)]
public sealed class Endpoint(IEtlConnectorDesignOperations operations) : ApiEndpoint<EtlDefinitionRequest, EtlConnectorDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlV2GetDefinition";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlConnectorDraftView> HandleAsync(EtlDefinitionRequest request, CancellationToken cancellationToken) =>
        operations.GetAsync(request, cancellationToken);
}
