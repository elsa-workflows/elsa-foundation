using Elsa.Etl.Runtime.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Runtime.Api.Endpoints.ListPartitions;

[Get("/etl/runs/{id}/partitions")]
[RequirePermission(EtlRuntimePermissions.ReadRuns)]
public sealed class Endpoint(IEtlRuntimeOperations operations) : ApiEndpoint<EtlRunPartitionsRequest, EtlRunPage<EtlRunPartitionView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlListRunPartitions";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlRunPage<EtlRunPartitionView>> HandleAsync(EtlRunPartitionsRequest request, CancellationToken cancellationToken) =>
        operations.ListPartitionsAsync(request, cancellationToken);
}
