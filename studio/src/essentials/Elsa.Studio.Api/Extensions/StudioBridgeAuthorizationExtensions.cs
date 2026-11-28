using Elsa.Studio.Api.Authorization;
using Microsoft.AspNetCore.Builder;

namespace Elsa.Studio.Api.Extensions;

public static class StudioBridgeAuthorizationExtensions
{
    /// <summary>
    /// Gates the endpoint(s) behind the Studio bridge's user-session gate plus any one of
    /// <paramref name="satisfyingPermissions"/> (see <see cref="StudioBridgeAuthorization.BuildPermissionPolicy"/>).
    /// The policy travels inline as endpoint metadata rather than by name, so a module mapped from a shell feature —
    /// which cannot add named policies to the host's <c>AuthorizationOptions</c> — gets exactly the semantics of the
    /// host's own host-control policies.
    /// </summary>
    public static TBuilder RequireStudioBridgePermission<TBuilder>(this TBuilder builder, params string[] satisfyingPermissions)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(StudioBridgeAuthorization.BuildPermissionPolicy(satisfyingPermissions));
}
