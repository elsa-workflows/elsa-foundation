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

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Studio:BackendBaseUrl"] = "https://backend.example/",
                ["Studio:BackendServerBaseUrl"] = "http://elsa-server:8080",
                // Configured server-side — the bridge needs it — but it must never leak into the browser script.
                ["Studio:BackendModuleManagementApiKey"] = ManagementKey,
                ["Studio:Auth:Enabled"] = "true"
            })
            .Build();

    [Fact]
    public void RuntimeScriptDoesNotContainTheManagementKeyValueEvenWhenConfigured()
    {
        var script = StudioRuntimeScript.Render(BuildConfiguration());

        Assert.DoesNotContain(ManagementKey, script);
    }

    [Fact]
    public void RuntimeScriptDoesNotContainTheManagementKeyFieldEvenWhenConfigured()
    {
        var script = StudioRuntimeScript.Render(BuildConfiguration());

        // The legacy browser-visible field is gone: neither the field name nor the underlying config key ride along.
        Assert.DoesNotContain("backendModuleManagementApiKey", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ManagementApiKey", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeScriptStillEmitsTheNonSecretConfigurationSurface()
    {
        // The removal is surgical: the non-secret surface the shell depends on is unchanged.
        var script = StudioRuntimeScript.Render(BuildConfiguration());

        Assert.Contains(StudioRuntimeScript.GlobalName, script);
        Assert.Contains("backendBaseUrl", script);
        Assert.Contains("https://backend.example/", script);
        Assert.DoesNotContain("http://elsa-server:8080", script);
        Assert.Contains("\"enabled\":true", script);
    }
}
