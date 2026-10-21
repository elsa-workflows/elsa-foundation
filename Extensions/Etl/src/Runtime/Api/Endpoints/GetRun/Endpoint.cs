using Elsa.Etl.Runtime.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Runtime.Api.Endpoints.GetRun;

[Get("/etl/runs/{id}")]
[RequirePermission(EtlRuntimePermissions.ReadRuns)]
public sealed class Endpoint(IEtlRuntimeOperations operations) : ApiEndpoint<EtlRunRequest, EtlRunDetailView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlGetRun";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<EtlRunDetailView> HandleAsync(EtlRunRequest request, CancellationToken cancellationToken) =>
        operations.GetRunAsync(request, cancellationToken);
}
