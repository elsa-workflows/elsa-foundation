using Elsa.Etl.Api;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ConnectorV2.ListBindings;

[Get("/etl/v2/bindings")]
[RequirePermission(EtlDesignPermissions.ReadBindings)]
public sealed class Endpoint(IEtlConnectorDesignOperations operations) : ApiEndpoint<EtlPageRequest, EtlPage<EtlConnectorBindingSummary>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlV2ListBindings";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlPage<EtlConnectorBindingSummary>> HandleAsync(EtlPageRequest request, CancellationToken cancellationToken) =>
        operations.ListBindingsAsync(request, cancellationToken);
}
