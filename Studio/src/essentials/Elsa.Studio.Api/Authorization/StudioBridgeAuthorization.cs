using Microsoft.AspNetCore.Authorization;

namespace Elsa.Studio.Api.Authorization;

/// <summary>
/// The public half of the Studio bridge's browser-request authorization (ADR 0037): the names a Studio module needs
/// to gate its own bridge endpoints behind the host's user-session gate without referencing the host application.
/// The host owns the authentication scheme and the handler that decides a
/// <see cref="StudioBridgePermissionRequirement"/>; a module only declares which permissions satisfy an endpoint.
/// </summary>
public static class StudioBridgeAuthorization
{
    /// <summary>The authentication scheme backing the host's bearer-introspection gate.</summary>
    public const string SchemeName = "StudioManagementBridgeAuth";

    /// <summary>
    /// The backend identity claim type carrying a granted permission key. Matches
    /// <c>Elsa.Foundation.Identity.Abstractions.Authorization.IdentityClaimTypes.Permission</c>; the host's introspection
    /// projects each permission from the backend session onto a claim of this type.
    /// </summary>
    public const string PermissionClaimType = "elsa.identity.permission";

    /// <summary>
    /// Builds a host-control permission policy: the bridge's bearer-introspection scheme, an authenticated user, and a
    /// <see cref="StudioBridgePermissionRequirement"/> over <paramref name="satisfyingPermissions"/>. Passing more than
    /// one key is how implication is expanded <i>locally</i>: e.g. a read surface lists both <c>read</c> and
    /// <c>manage</c>, so a <c>manage</c>-only holder satisfies the read gate without Studio depending on the backend to
    /// expand <c>manage ⇒ read</c>. An unauthenticated request is challenged (401); a signed-in user missing every key
    /// fails the requirement while remaining authenticated, so ASP.NET returns 403 (not 401).
    /// </summary>
    public static AuthorizationPolicy BuildPermissionPolicy(params string[] satisfyingPermissions) =>
        new AuthorizationPolicyBuilder(SchemeName)
            .RequireAuthenticatedUser()
            .AddRequirements(new StudioBridgePermissionRequirement(satisfyingPermissions))
            .Build();
}
