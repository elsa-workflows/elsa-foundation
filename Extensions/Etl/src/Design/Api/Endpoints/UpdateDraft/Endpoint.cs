using Elsa.Etl.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.UpdateDraft;

[Put("/etl/definitions/{id}/draft")]
[RequirePermission(EtlDesignPermissions.ManageDefinitions)]
[EtlBoundedBody]
public sealed class Endpoint(IEtlDesignOperations operations) : ApiEndpoint<EtlUpdateDraftRequest, EtlDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlUpdateDraft";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<EtlDraftView> HandleAsync(EtlUpdateDraftRequest request, CancellationToken cancellationToken) =>
        operations.UpdateAsync(request, cancellationToken);
}
