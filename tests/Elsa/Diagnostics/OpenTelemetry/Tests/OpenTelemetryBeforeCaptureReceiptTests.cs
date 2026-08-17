using System.Security.Cryptography;
using System.Text.Json;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class OpenTelemetryBeforeCaptureReceiptTests
{
    [Fact]
    public async Task Before_fixtures_have_a_reproducible_deleted_fastendpoints_capture_receipt()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Baselines");
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "otel-before-capture-receipt.json")));
        var root = receipt.RootElement;

        Assert.Equal("db6e363db", root.GetProperty("sourceCommit").GetString());
        Assert.Equal("7c6e58784", root.GetProperty("captureSourceCommit").GetString());
        Assert.Contains("git worktree add --detach", root.GetProperty("captureCommand").GetString(), StringComparison.Ordinal);
        Assert.Contains("-p:IsTestProject=true", root.GetProperty("captureCommand").GetString(), StringComparison.Ordinal);
        Assert.Contains("OpenTelemetryFastEndpointsCaptureHarnessTests", root.GetProperty("captureImplementation").GetString(), StringComparison.Ordinal);

        Assert.Equal("d3066a8c8c0eacbdd9409a340b2587c07394336088ae9449ddeb3d7e0be291cc", await HashAsync(Path.Combine(directory, "otel-http-openapi-fastendpoints.json")));
        Assert.Equal("6c5fecfb6674f628ae4d22730cfe6682777ebd372d8b0b97f08a38904526e272", await HashAsync(Path.Combine(directory, "otel-http-authenticated-fastendpoints.json")));
        Assert.Equal("4fcdcdd94ef0d386b1ab561b16fee653d8971171953b2a1e7862190843a39977", await HashAsync(Path.Combine(directory, "otel-http-binding-fastendpoints.json")));
        Assert.Equal("c2bb7a946582a463844b7a228bfa2eb631bfa043872f4bd071702fee43511534", await HashAsync(Path.Combine(directory, "otel-http-redirect-fastendpoints.json")));
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }
}
