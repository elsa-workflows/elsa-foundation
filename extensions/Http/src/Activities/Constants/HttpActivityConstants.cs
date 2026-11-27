namespace Elsa.Activities.Http.Constants;

/// <summary>
/// Well-known identifiers for the HTTP activity package. The client name keys the named
/// <see cref="System.Net.Http.HttpClient"/> that <c>SendHttpRequest</c> resolves from
/// <see cref="System.Net.Http.IHttpClientFactory"/>, so the transport (timeout, redirect and pooling policy)
/// is configured in one place (<c>ActivitiesHttpFeature</c>) rather than per activity instance.
/// </summary>
public static class HttpActivityConstants
{
    /// <summary>The <see cref="System.Net.Http.IHttpClientFactory"/> logical client name used by outbound HTTP activities.</summary>
    public const string HttpClientName = "Elsa.Activities.Http";
}
