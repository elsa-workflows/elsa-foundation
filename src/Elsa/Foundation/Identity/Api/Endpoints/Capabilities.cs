using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Api.Models;

namespace Elsa.Foundation.Identity.Api.Endpoints;

internal sealed class Capabilities(
    IAuthenticationProviderResolver providers,
    IOwnershipModeProvider ownershipModeProvider,
    IEffectiveCapabilitiesResolver capabilitiesResolver,
    IPermissionCatalog permissionCatalog)
    : ElsaEndpointWithoutRequest<IdentityCapabilitiesResponse>
{
    public override void Configure()
    {
        Get(IdentityRouteConstants.GetRoute("capabilities"));
        ConfigurePermissions(DefaultIdentityPermissionKeys.IdentityProvidersRead);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var ownership = await ownershipModeProvider.GetAsync(cancellationToken: ct);
        var providerDescriptors = await providers.ListAsync(ct);
        var response = new IdentityCapabilitiesResponse(
            ownership.Mode,
            providerDescriptors
                .Select(x => new IdentityProviderCapabilitiesResponse(
                    x.Id,
                    x.Kind,
                    x.DisplayName,
                    x.IsDefault,
                    x.Enabled,
                    capabilitiesResolver.Resolve(ownership, x.Capabilities)))
                .ToList(),
            permissionCatalog.List()
                .Select(x => new IdentityPermissionResponse(
                    x.Key,
                    x.DisplayName,
                    x.Category,
                    x.Description,
                    x.Implies?.ToArray() ?? []))
                .ToList());

        await Send.OkAsync(response, ct);
    }
}
