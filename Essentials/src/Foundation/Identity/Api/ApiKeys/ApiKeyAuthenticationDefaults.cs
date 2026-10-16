namespace Elsa.Foundation.Identity.Api.ApiKeys;

/// <summary>The names an application's API key authenticates under.</summary>
public static class ApiKeyAuthenticationDefaults
{
    /// <summary>
    /// The ASP.NET Core authentication scheme, and the authentication type of the principal it produces. The OpenIddict
    /// scheme selector forwards a request carrying <see cref="AuthorizationScheme"/> credentials here.
    /// </summary>
    public const string AuthenticationScheme = "Elsa.Identity.ApiKey";

    /// <summary>
    /// The <c>Authorization</c> header scheme a client sends the key with: <c>Authorization: ApiKey {key}</c>. Elsa 3's
    /// API-key handler and API client use the same scheme, so an Elsa 3 integration only needs a key issued here.
    /// </summary>
    public const string AuthorizationScheme = "ApiKey";

    /// <summary>The provider recorded on an API-key principal's <c>elsa.identity.provider</c> claim.</summary>
    public const string ProviderId = "api-key";
}
