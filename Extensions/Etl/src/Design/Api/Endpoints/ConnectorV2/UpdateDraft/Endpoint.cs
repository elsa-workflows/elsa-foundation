using Elsa.Etl.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ConnectorV2.UpdateDraft;

[Put("/etl/v2/definitions/{id}/draft")]
[RequirePermission(EtlDesignPermissions.ManageDefinitions)]
[EtlBoundedBody]
public sealed class Endpoint(IEtlConnectorDesignOperations operations) : ApiEndpoint<EtlConnectorUpdateDraftRequest, EtlConnectorDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlV2UpdateDraft";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<EtlConnectorDraftView> HandleAsync(EtlConnectorUpdateDraftRequest request, CancellationToken cancellationToken) =>
        operations.UpdateAsync(request, cancellationToken);
}
