using System.Text.Json;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

/// <summary>Locks the real FastEndpoints capture before the owner migration deletes its registrations.</summary>
public sealed class OpenTelemetryFastEndpointsBeforeBaselineTests
{
    [Fact]
    public void Captured_before_oracle_contains_all_eleven_shell_routes_and_consumed_openapi()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baselines", "otel-http-openapi-fastendpoints.json")));
        var root = document.RootElement;
        Assert.Equal("c04d8dbbe", root.GetProperty("capturedAt").GetString());
        Assert.Equal(11, root.GetProperty("routes").EnumerateArray().Count(entry => entry.GetProperty("route").GetString()?.StartsWith("/diagnostics/opentelemetry", StringComparison.Ordinal) == true || entry.GetProperty("route").GetString()?.StartsWith("/_elsa/studio/diagnostics/opentelemetry", StringComparison.Ordinal) == true || entry.GetProperty("route").GetString()?.StartsWith("/elsa/otlp", StringComparison.Ordinal) == true));
        Assert.Equal(11, root.GetProperty("http").GetArrayLength());
        Assert.True(root.GetProperty("openApi").GetProperty("paths").EnumerateObject().Count() >= 11);
    }
}
