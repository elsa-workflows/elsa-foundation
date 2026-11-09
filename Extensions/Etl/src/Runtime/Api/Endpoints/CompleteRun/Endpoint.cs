using Elsa.Etl.Api;
using Elsa.Etl.Runtime.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Runtime.Api.Endpoints.CompleteRun;

[Post("/etl/runs/{id}/complete")]
[RequirePermission(EtlRuntimePermissions.CompleteRuns)]
[EtlBoundedBody(4096)]
public sealed class Endpoint(IEtlRuntimeOperations operations) : ApiEndpoint<EtlRunRequest, EtlRunCompletionView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlCompleteRun";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
        options.BodyMode = EndpointBodyMode.None;
    }

    public override Task<EtlRunCompletionView> HandleAsync(EtlRunRequest request, CancellationToken cancellationToken) =>
        operations.CompleteAsync(request, cancellationToken);
}
