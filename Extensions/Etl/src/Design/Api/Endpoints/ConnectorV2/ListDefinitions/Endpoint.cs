using Elsa.Etl.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ConnectorV2.ListDefinitions;

[Get("/etl/v2/definitions")]
[RequirePermission(EtlDesignPermissions.ReadDefinitions)]
public sealed class Endpoint(IEtlConnectorDesignOperations operations) : ApiEndpoint<EtlPageRequest, EtlPage<EtlDefinitionSummaryView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlV2ListDefinitions";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlPage<EtlDefinitionSummaryView>> HandleAsync(EtlPageRequest request, CancellationToken cancellationToken) =>
        operations.ListDefinitionsAsync(request, cancellationToken);
}
