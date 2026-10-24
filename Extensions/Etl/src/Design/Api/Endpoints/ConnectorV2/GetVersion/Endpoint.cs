using Elsa.Etl.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ConnectorV2.GetVersion;

[Get("/etl/v2/definitions/{id}/versions/{version}")]
[RequirePermission(EtlDesignPermissions.ReadDefinitions)]
public sealed class Endpoint(IEtlConnectorDesignOperations operations) : ApiEndpoint<EtlVersionRequest, EtlConnectorPublishedArtifactView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlV2GetVersion";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlConnectorPublishedArtifactView> HandleAsync(EtlVersionRequest request, CancellationToken cancellationToken) =>
        operations.GetVersionAsync(request, cancellationToken);
}
