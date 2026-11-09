using Elsa.Etl.Api;
using Elsa.Etl.Runtime.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Runtime.Api.Endpoints.CancelRun;

[Post("/etl/runs/{id}/cancel")]
[RequirePermission(EtlRuntimePermissions.CancelRuns)]
[EtlBoundedBody(4096)]
public sealed class Endpoint(IEtlRuntimeOperations operations) : ApiEndpoint<EtlRunRequest, EtlRunCancellationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlCancelRun";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
        options.BodyMode = EndpointBodyMode.None;
    }

    public override Task<EtlRunCancellationView> HandleAsync(EtlRunRequest request, CancellationToken cancellationToken) =>
        operations.CancelAsync(request, cancellationToken);
}
