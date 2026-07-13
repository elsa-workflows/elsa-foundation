using System.Text.Json;

namespace Elsa.Studio.Web;

/// <summary>
/// Builds the <c>/studio-runtime.js</c> body that seeds <c>window.__ELSA_STUDIO_RUNTIME__</c> in the browser.
///
/// <para>This is browser-visible configuration, so it must never contain a server-side secret. In particular the
/// backend host management key (<c>Studio:BackendModuleManagementApiKey</c>) is intentionally absent (ADR 0037): it is
/// a server-side-only credential the Studio management bridge attaches on Studio→backend calls, and browser clients
/// must never carry a host management key. The extracted, testable shape lets a regression test assert the key is not
/// present even when it is configured.</para>
/// </summary>
internal static class StudioRuntimeScript
{
    public const string GlobalName = "window.__ELSA_STUDIO_RUNTIME__";

    /// <summary>Builds the browser runtime config object surfaced to the SPA. Contains no server-side secrets.</summary>
    public static Dictionary<string, object?> BuildRuntimeConfig(IConfiguration configuration) =>
        new()
        {
            ["backendBaseUrl"] = configuration["Studio:BackendBaseUrl"] ?? string.Empty,
            ["hostId"] = configuration["Studio:HostId"] ?? "default",
            // Surface the user-auth seam so the shell can attach real bearer tokens (and 401-refresh-retry) against the
            // backend identity endpoints. Omitted endpoints fall back to the SDK defaults (`/_elsa/identity/token`);
            // when Enabled is false the shell keeps booting anonymously.
            ["auth"] = BuildAuthRuntimeConfig(configuration),
            ["workflows"] = BuildWorkflowsRuntimeConfig(configuration),
            ["attention"] = new Dictionary<string, object?>
            {
                ["hostApiEnabled"] = configuration.GetValue("Studio:Attention:HostApiEnabled", defaultValue: false)
            },
            ["dashboard"] = BuildDashboardRuntimeConfig(configuration)
        };

    /// <summary>Renders the full <c>/studio-runtime.js</c> body.</summary>
    public static string Render(IConfiguration configuration) =>
        $"{GlobalName} = {JsonSerializer.Serialize(BuildRuntimeConfig(configuration))};";

    private static Dictionary<string, object?> BuildAuthRuntimeConfig(IConfiguration configuration)
    {
        var auth = new Dictionary<string, object?>
        {
            ["enabled"] = configuration.GetValue("Studio:Auth:Enabled", defaultValue: false)
        };

        var tokenEndpoint = configuration["Studio:Auth:TokenEndpoint"];
        if (!string.IsNullOrWhiteSpace(tokenEndpoint))
            auth["tokenEndpoint"] = tokenEndpoint;

        var refreshEndpoint = configuration["Studio:Auth:RefreshEndpoint"];
        if (!string.IsNullOrWhiteSpace(refreshEndpoint))
            auth["refreshEndpoint"] = refreshEndpoint;

        return auth;
    }

    private static Dictionary<string, object?> BuildWorkflowsRuntimeConfig(IConfiguration configuration) =>
        new()
        {
            ["autosaveEnabledByDefault"] = configuration.GetValue("Studio:Workflows:AutosaveEnabledByDefault", defaultValue: true)
        };

    private static Dictionary<string, object?> BuildDashboardRuntimeConfig(IConfiguration configuration) =>
        new()
        {
            ["defaultRefreshIntervalMs"] = (long)TimeSpan.FromMinutes(
                configuration.GetValue("Studio:Dashboard:DefaultRefreshIntervalMinutes", 5)).TotalMilliseconds,
            ["widgetTimeoutMs"] = (long)TimeSpan.FromSeconds(
                configuration.GetValue("Studio:Dashboard:WidgetTimeoutSeconds", 10)).TotalMilliseconds,
            ["pinnedWidgetIds"] = configuration.GetSection("Studio:Dashboard:PinnedWidgetIds").Get<string[]>() ?? []
        };
}
