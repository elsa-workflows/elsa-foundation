using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Api.Endpoints.Features.Apply;

[Post("modularity/features/apply")]
[RequirePermission(ModuleManagementPermissionKeys.Manage)]
public sealed class Endpoint(IFeatureManagementService service) : ApiEndpoint<FeatureApplyRequest, FeatureApplyResult>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "Apply";
        options.Accepts = ["application/json"];
        // Required, not content-type-gated: the mapper read the body regardless of the declared
        // content type and published a legacy-envelope 400 for an absent or unreadable payload.
        options.BodyMode = EndpointBodyMode.Required;
    }

    public override Task<FeatureApplyResult> HandleAsync(FeatureApplyRequest request, CancellationToken cancellationToken) =>
        service.ApplyAsync(request, cancellationToken);
}
