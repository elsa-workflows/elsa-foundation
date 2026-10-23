using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.GetDefinition;

[Get("/etl/definitions/{id}")]
[RequirePermission(EtlDesignPermissions.ReadDefinitions)]
public sealed class Endpoint(IEtlDesignOperations operations) : ApiEndpoint<EtlDefinitionRequest, EtlDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlGetDefinition";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlDraftView> HandleAsync(EtlDefinitionRequest request, CancellationToken cancellationToken) =>
        operations.GetAsync(request, cancellationToken);
}
