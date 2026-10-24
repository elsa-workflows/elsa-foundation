using Elsa.Etl.Runtime.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Runtime.Api.Endpoints.ListRuns;

[Get("/etl/runs")]
[RequirePermission(EtlRuntimePermissions.ReadRuns)]
public sealed class Endpoint(IEtlRuntimeOperations operations) : ApiEndpoint<EtlRunPageRequest, EtlRunPage<EtlRunSummaryView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlListRuns";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlRunPage<EtlRunSummaryView>> HandleAsync(EtlRunPageRequest request, CancellationToken cancellationToken) =>
        operations.ListRunsAsync(request, cancellationToken);
}
