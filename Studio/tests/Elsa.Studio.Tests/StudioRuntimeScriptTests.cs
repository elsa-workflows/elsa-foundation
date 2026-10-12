using Elsa.Studio.Web;
using Microsoft.Extensions.Configuration;

namespace Elsa.Studio.Tests;

/// <summary>
/// Regression guard for ADR 0037: the browser runtime script (<c>/studio-runtime.js</c>) must never expose the backend
/// host management key. The key stays a server-side-only Studio setting the management bridge uses; it must not appear
/// in the browser runtime config — by value or by field — even when it is configured.
/// </summary>
public sealed class StudioRuntimeScriptTests
{
    private const string ManagementKey = "s3cr3t-management-key";

    // Rendered from the fully-configured surface (management key included) shared by the leak/surface guards below.
    private readonly string _script = StudioRuntimeScript.Render(BuildConfiguration());

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Studio:BackendBaseUrl"] = "https://backend.example/",
                ["Studio:BackendServerBaseUrl"] = "http://elsa-server:8080",
                ["Studio:HostId"] = "studio-primary",
                ["Studio:Dashboard:DefaultRefreshIntervalMinutes"] = "15",
                ["Studio:Dashboard:WidgetTimeoutSeconds"] = "12",
                ["Studio:Dashboard:PinnedWidgetIds:0"] = "attention.queue",
                // Configured server-side — the bridge needs it — but it must never leak into the browser script.
                ["Studio:BackendModuleManagementApiKey"] = ManagementKey,
                ["Studio:Auth:Enabled"] = "true",
                ["Studio:ActivityDefinitions:LocalRecovery:Enabled"] = "true",
                ["Studio:ActivityDefinitions:LocalRecovery:TtlMinutes"] = "90"
            })
            .Build();

    [Fact]
    public void RuntimeScriptDoesNotContainTheManagementKeyValueEvenWhenConfigured()
    {
        Assert.DoesNotContain(ManagementKey, _script);
    }

    [Fact]
    public void RuntimeScriptDoesNotContainTheManagementKeyFieldEvenWhenConfigured()
    {
        // The legacy browser-visible field is gone: neither the field name nor the underlying config key ride along.
        Assert.DoesNotContain("backendModuleManagementApiKey", _script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ManagementApiKey", _script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeScriptStillEmitsTheNonSecretConfigurationSurface()
    {
        // The removal is surgical: the non-secret surface the shell depends on is unchanged.
        Assert.Contains(StudioRuntimeScript.GlobalName, _script);
        Assert.Contains("backendBaseUrl", _script);
        Assert.Contains("https://backend.example/", _script);
        Assert.DoesNotContain("http://elsa-server:8080", _script);
        Assert.Contains("\"enabled\":true", _script);
        Assert.Contains("activityDefinitions", _script);
        Assert.Contains("localRecovery", _script);
        Assert.Contains("\"ttlMinutes\":90", _script);
        Assert.Contains("\"hostId\":\"studio-primary\"", _script);
        Assert.Contains("\"defaultRefreshIntervalMs\":900000", _script);
        Assert.Contains("\"widgetTimeoutMs\":12000", _script);
        Assert.Contains("\"pinnedWidgetIds\":[\"attention.queue\"]", _script);
    }

    [Fact]
    public void RuntimeScriptUsesStableDashboardDefaultsWhenConfigurationIsAbsent()
    {
        var script = StudioRuntimeScript.Render(new ConfigurationBuilder().Build());

        Assert.Contains("\"hostId\":\"default\"", script);
        Assert.Contains("\"defaultRefreshIntervalMs\":300000", script);
        Assert.Contains("\"widgetTimeoutMs\":10000", script);
        Assert.Contains("\"pinnedWidgetIds\":[]", script);
    }
}
