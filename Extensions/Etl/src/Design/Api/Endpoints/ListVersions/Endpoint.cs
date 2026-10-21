using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ListVersions;

[Get("/etl/definitions/{id}/versions")]
[RequirePermission(EtlDesignPermissions.ReadDefinitions)]
public sealed class Endpoint(IEtlDesignOperations operations) : ApiEndpoint<EtlVersionsRequest, EtlPage<EtlPublishedVersionView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlListVersions";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlPage<EtlPublishedVersionView>> HandleAsync(EtlVersionsRequest request, CancellationToken cancellationToken) =>
        operations.ListVersionsAsync(request, cancellationToken);
}
