using Elsa.Etl.Design.Api.Models;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api.Endpoints.ValidateDraft;

[Post("/etl/definitions/{id}/validate")]
[RequirePermission(EtlDesignPermissions.ManageDefinitions)]
[EtlBoundedBody]
public sealed class Endpoint(IEtlDesignOperations operations) : ApiEndpoint<EtlValidateRequest, EtlValidationView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "EtlValidateDraft";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<EtlValidationView> HandleAsync(EtlValidateRequest request, CancellationToken cancellationToken) =>
        operations.ValidateAsync(request, cancellationToken);
}
