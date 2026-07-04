using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Api.Models;

namespace Elsa.Foundation.Identity.Api.Endpoints;

internal sealed class Bootstrap(IAuthenticationProviderResolver providers, IOwnershipModeProvider ownershipModeProvider)
    : ElsaEndpointWithoutRequest<IdentityBootstrapResponse>
{
    public override void Configure()
    {
        Get(IdentityRouteConstants.GetRoute("bootstrap"));
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var ownership = await ownershipModeProvider.GetAsync(cancellationToken: ct);
        var providerDescriptors = await providers.ListAsync(ct);
        var response = new IdentityBootstrapResponse(
            ownership.Mode,
            providerDescriptors.Select(x => new IdentityProviderResponse(x.Id, x.Kind, x.DisplayName, x.IsDefault, x.Enabled, x.Challenge)).ToList());

        await Send.OkAsync(response, ct);
    }
}
