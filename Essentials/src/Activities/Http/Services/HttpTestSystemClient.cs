namespace Elsa.Activities.Http.Services;

/// <summary>
/// One module-owned transport per admitted test-system operation. Ordinary factory handlers, defaults and cookie
/// pools never participate. Supporting another handler pipeline requires a separately qualified module profile.
/// </summary>
internal static class HttpTestSystemClient
{
    internal static HttpClient Create() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseDefaultCredentials = false,
        Credentials = null,
        ClientCertificateOptions = ClientCertificateOption.Manual,
        PreAuthenticate = false,
        UseProxy = false,
        Proxy = null,
        DefaultProxyCredentials = null
    }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
}
