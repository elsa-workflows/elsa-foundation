using CShells.FastEndpoints.Contracts;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using FastEndpoints;

namespace Elsa.Foundation.Identity.Api.Configurators;

/// <summary>
/// Aligns FastEndpoints' permission/role claim types with the claim types the Elsa identity tokens carry, so
/// that endpoints secured with <c>ConfigurePermissions()</c> (FastEndpoints' <c>Permissions([...])</c>) are
/// satisfied by a bearer issued by the OpenIddict module.
/// </summary>
/// <remarks>
/// FastEndpoints defaults its permissions claim type to <c>"permissions"</c> and its role claim type to the
/// standard <see cref="System.Security.Claims.ClaimTypes.Role"/>. The first-party access token, however,
/// emits permission claims as <see cref="IdentityClaimTypes.Permission"/> (<c>elsa.identity.permission</c>)
/// and roles as <see cref="IdentityClaimTypes.Role"/>. Without this alignment a fully-authorized principal
/// would still be denied by <c>Permissions()</c> because the claim type would not match. Registered by the
/// identity API feature (so it applies wherever the identity endpoints are hosted); it only touches the
/// process-static FastEndpoints security claim-type configuration and is idempotent.
/// </remarks>
internal sealed class IdentityClaimTypeFastEndpointsConfigurator : IFastEndpointsConfigurator
{
    public void Configure(Config config)
    {
        config.Security.PermissionsClaimType = IdentityClaimTypes.Permission;
        config.Security.RoleClaimType = IdentityClaimTypes.Role;
    }
}
