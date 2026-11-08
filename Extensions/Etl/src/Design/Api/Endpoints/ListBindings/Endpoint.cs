using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ListBindings;

[Get("/etl/bindings")]
[RequirePermission(EtlDesignPermissions.ReadBindings)]
public sealed class Endpoint(IEtlDesignOperations operations) : ApiEndpoint<EtlPageRequest, EtlPage<EtlBindingSummary>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlListBindings";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlPage<EtlBindingSummary>> HandleAsync(EtlPageRequest request, CancellationToken cancellationToken) =>
        operations.ListBindingsAsync(request, cancellationToken);
}
