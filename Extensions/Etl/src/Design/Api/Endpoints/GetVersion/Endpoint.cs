using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.GetVersion;

[Get("/etl/definitions/{id}/versions/{version}")]
[RequirePermission(EtlDesignPermissions.ReadDefinitions)]
public sealed class Endpoint(IEtlDesignOperations operations) : ApiEndpoint<EtlVersionRequest, EtlPublishedArtifactView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlGetVersion";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlPublishedArtifactView> HandleAsync(EtlVersionRequest request, CancellationToken cancellationToken) =>
        operations.GetVersionAsync(request, cancellationToken);
}
