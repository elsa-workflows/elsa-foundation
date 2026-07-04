namespace Elsa.Foundation.Identity.AspNetCoreIdentity;

/// <summary>
/// Shared, stable constants for the ASP.NET Core Identity provider: the provider id, the cookie
/// authentication scheme used for first-party sign-in, and the login route. These are referenced from the
/// provider module, the sign-in endpoints, and the challenge redirect, so they live in one place.
/// </summary>
public static class AspNetCoreIdentityDefaults
{
    /// <summary>The provider id used in <c>bootstrap</c>/<c>capabilities</c> and the challenge redirect.</summary>
    public const string ProviderId = "aspnetcore-identity";

    /// <summary>The cookie authentication scheme that first-party sign-in issues and logout clears.</summary>
    public const string CookieScheme = "Elsa.Identity.Cookie";

    /// <summary>The tenant assigned to first-party users when none is supplied.</summary>
    public const string DefaultTenantId = "default";

    /// <summary>Base route for the first-party sign-in surface, under the shared identity prefix.</summary>
    public const string LoginRoute = "_elsa/identity/login";
}
