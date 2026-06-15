using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Modularity.Api.Constants;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Api.Endpoints;

internal sealed class List(IFeatureManagementService featureManagementService) : ElsaEndpointWithoutRequest<FeatureCatalogResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.DomainPrefix);
        ConfigurePermissions();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var response = await featureManagementService.GetCatalogAsync(ct);
        await Send.OkAsync(response, ct);
    }
}
