using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ListDefinitions;

[Get("/etl/definitions")]
[RequirePermission(EtlDesignPermissions.ReadDefinitions)]
public sealed class Endpoint(IEtlDesignOperations operations) : ApiEndpoint<EtlPageRequest, EtlPage<EtlDefinitionSummaryView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlListDefinitions";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlPage<EtlDefinitionSummaryView>> HandleAsync(EtlPageRequest request, CancellationToken cancellationToken) =>
        operations.ListDefinitionsAsync(request, cancellationToken);
}
