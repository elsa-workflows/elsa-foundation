using Microsoft.Extensions.Configuration;

namespace Elsa.Studio.Api.Options;

/// <summary>
/// Server-side configuration for Studio→backend calls, bound from <c>Studio:BackendServerBaseUrl</c> and
/// <c>Studio:BackendModuleManagementApiKey</c>. <c>Studio:BackendBaseUrl</c> remains the browser-facing URL and is
/// used as the server-side fallback for single-host deployments. The management key is held server-side only and is
/// never emitted to the browser by the bridge.
/// </summary>
public sealed record StudioBackendManagementOptions(string? BackendBaseUrl, string? ManagementApiKey)
{
    public const string BackendBaseUrlConfigurationKey = "Studio:BackendBaseUrl";
    public const string BackendServerBaseUrlConfigurationKey = "Studio:BackendServerBaseUrl";
    public const string ManagementApiKeyConfigurationKey = "Studio:BackendModuleManagementApiKey";

    /// <summary>
    /// The request header the backend Elsa host expects the management key on. This is the backend host-control
    /// contract; Studio attaches it server-side on Studio→backend calls only. The browser never carries it (ADR 0037).
    /// </summary>
    public const string ManagementApiKeyHeaderName = "X-Elsa-Module-Management-Key";

    /// <summary>
    /// The timeout for browser-facing Studio→backend reads (bridge status, registry, module capabilities). Short, so
    /// those reads stay snappy even when the backend is slow to answer.
    /// </summary>
    public static readonly TimeSpan ReadRequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Both halves of the Studio→backend credential pair are present. When false every bridge surface fails
    /// closed to <c>unconfigured</c> with zero outbound calls (ADR 0037).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BackendBaseUrl) && !string.IsNullOrWhiteSpace(ManagementApiKey);

    /// <summary>The resolved server-side backend URL without its trailing slash, or null when unset.</summary>
    public string? NormalizedBackendBaseUrl => string.IsNullOrWhiteSpace(BackendBaseUrl) ? null : BackendBaseUrl.TrimEnd('/');

    /// <summary>
    /// Resolves the URL used by Studio's server-side backend clients. The dedicated server URL is preferred; the
    /// legacy/shared URL remains a fallback so existing deployments keep their current behaviour.
    /// </summary>
    public static string? ResolveServerBaseUrl(IConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration[BackendServerBaseUrlConfigurationKey])
            ? configuration[BackendBaseUrlConfigurationKey]
            : configuration[BackendServerBaseUrlConfigurationKey];

    public static StudioBackendManagementOptions FromConfiguration(IConfiguration configuration) =>
        new(ResolveServerBaseUrl(configuration), configuration[ManagementApiKeyConfigurationKey]);

    /// <summary>
    /// Points a Studio→backend <see cref="HttpClient"/> at the resolved backend with the given timeout. When no backend
    /// base URL is configured no <c>BaseAddress</c> is set — the clients still resolve and fail closed to
    /// <c>unconfigured</c> without issuing any request.
    /// </summary>
    public void ConfigureBackendClient(HttpClient client, TimeSpan timeout)
    {
        client.Timeout = timeout;
        if (!string.IsNullOrWhiteSpace(BackendBaseUrl))
            client.BaseAddress = new Uri(BackendBaseUrl, UriKind.Absolute);
    }
}
