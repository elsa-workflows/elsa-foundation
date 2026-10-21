using Elsa.Etl.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ConnectorV2.PublishVersion;

[Post("/etl/v2/definitions/{id}/publish")]
[RequirePermission(EtlDesignPermissions.PublishDefinitions)]
[EtlBoundedBody]
public sealed class Endpoint(IEtlConnectorDesignOperations operations) : ApiEndpoint<EtlPublishRequest, EtlPublicationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlV2PublishVersion";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<EtlPublicationView> HandleAsync(EtlPublishRequest request, CancellationToken cancellationToken) =>
        operations.PublishAsync(request, cancellationToken);
}
