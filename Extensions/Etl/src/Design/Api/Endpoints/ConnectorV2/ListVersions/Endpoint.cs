using Elsa.Etl.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ConnectorV2.ListVersions;

[Get("/etl/v2/definitions/{id}/versions")]
[RequirePermission(EtlDesignPermissions.ReadDefinitions)]
public sealed class Endpoint(IEtlConnectorDesignOperations operations) : ApiEndpoint<EtlVersionsRequest, EtlPage<EtlPublishedVersionView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlV2ListVersions";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlPage<EtlPublishedVersionView>> HandleAsync(EtlVersionsRequest request, CancellationToken cancellationToken) =>
        operations.ListVersionsAsync(request, cancellationToken);
}
