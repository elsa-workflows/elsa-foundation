using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;

namespace Elsa.Api.Capabilities.Endpoints;

internal sealed class GetCapabilities(IApiCapabilityCatalog catalog) : ElsaEndpointWithoutRequest<ApiCapabilitiesDocument>
{
    public override void Configure()
    {
        Get("capabilities");
        ConfigurePermissions(PermissionNames.ApiCapabilitiesRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var document = await catalog.GetAsync(cancellationToken);
        await Send.OkAsync(document, cancellationToken);
    }
}
