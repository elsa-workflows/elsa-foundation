using Elsa.Etl.Runtime.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Runtime.Api.Endpoints.ListConnectorPartitions;

[Get("/etl/runs/{id}/connector-partitions")]
[RequirePermission(EtlRuntimePermissions.ReadRuns)]
public sealed class Endpoint(IEtlRuntimeOperations operations) : ApiEndpoint<EtlRunPartitionsRequest, EtlRunPage<EtlRunConnectorPartitionView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlListRunConnectorPartitions";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlRunPage<EtlRunConnectorPartitionView>> HandleAsync(EtlRunPartitionsRequest request, CancellationToken cancellationToken) =>
        operations.ListConnectorPartitionsAsync(request, cancellationToken);
}
