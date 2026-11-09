using Elsa.Etl.Api;
using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ConnectorV2.ValidateDraft;

[Post("/etl/v2/definitions/{id}/validate")]
[RequirePermission(EtlDesignPermissions.ManageDefinitions)]
[EtlBoundedBody]
public sealed class Endpoint(IEtlConnectorDesignOperations operations) : ApiEndpoint<EtlValidateRequest, EtlValidationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlV2ValidateDraft";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<EtlValidationView> HandleAsync(EtlValidateRequest request, CancellationToken cancellationToken) =>
        operations.ValidateAsync(request, cancellationToken);
}
