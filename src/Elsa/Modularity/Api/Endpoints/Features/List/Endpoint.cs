using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Api.Constants;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Api.Endpoints.Features.List;

[Get(RouteConstants.DomainPrefix)]
[RequirePermission(ModuleManagementPermissionKeys.Read)]
public sealed class Endpoint(IFeatureManagementService service) : ApiEndpointWithoutRequest<FeatureCatalogResponse>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "List";

    public override Task<FeatureCatalogResponse> HandleAsync(CancellationToken cancellationToken) =>
        service.GetCatalogAsync(cancellationToken);
}
