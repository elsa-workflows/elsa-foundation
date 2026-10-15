namespace Elsa.Integrations.Sdk.OAuth;

/// <summary>
/// Plan D2: the OAuth callback is a backend route built from <see cref="PublicBaseUrl"/>.
/// Production Control (#801) owns the hosted endpoint; this stub holds the path contract.
/// </summary>
public sealed class OAuthCallbackOptions
{
    public const string Path = "/_elsa/connections/oauth/callback";

    /// <summary>Public origin of the Elsa host, no trailing slash. Combined with <see cref="Path"/>.</summary>
    public string PublicBaseUrl { get; set; } = "https://localhost";

    public Uri CallbackUri
    {
        get
        {
            var origin = PublicBaseUrl.Trim().TrimEnd('/');
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var baseUri))
                throw new InvalidOperationException("PublicBaseUrl must be an absolute URL.");
            return new Uri(baseUri, Path);
        }
    }
}
