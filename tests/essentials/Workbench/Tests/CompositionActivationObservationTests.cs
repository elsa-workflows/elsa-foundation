using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

public sealed class CompositionActivationObservationTests
{
    private const string ObservationPath = "/_admin/composition/default-shell/observation";
    private const string ReloadPath = "/_admin/composition/default-shell/reload";
    private const string Canary = "composition-observation-secret-canary-2041";

    [Fact]
    public async Task Observation_and_reload_require_the_management_key()
    {
        await using var host = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);
        var initial = await ReadinessGenerationAsync(host);

        using var anonymousRead = await host.Client.GetAsync(ObservationPath);
        using var anonymousReload = await host.Client.PostAsync(ReloadPath, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousReload.StatusCode);

        using var wrongKey = new HttpClient { BaseAddress = host.Client.BaseAddress };
        wrongKey.DefaultRequestHeaders.Add(WorkbenchProcess.ManagementKeyHeader, "not-the-management-key");
        using var wrongKeyRead = await wrongKey.GetAsync(ObservationPath);
        using var wrongKeyReload = await wrongKey.PostAsync(ReloadPath, null);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKeyRead.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKeyReload.StatusCode);
        Assert.Equal(initial, await ReadinessGenerationAsync(host));
    }

    [Fact]
    public async Task Authorized_reload_returns_safe_default_shell_readback()
    {
        await using var host = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);
        var before = await ReadObservationAsync(host);

        Assert.Equal("default", before.Shell);
        Assert.True(before.ActiveGeneration > 0);
        Assert.True(before.Ready);
        Assert.Equal("unverified", before.CandidateMatch);

        using var response = await host.ManagementClient.PostAsync(ReloadPath, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReloadObservation>();

        Assert.NotNull(body);
        Assert.Equal("ready", body.Outcome);
        Assert.Equal(before.ActiveGeneration, body.PreviousGeneration);
        Assert.True(body.ReportedGeneration > body.PreviousGeneration);
        Assert.Equal(body.ReportedGeneration, body.ActiveGeneration);
        Assert.True(body.Ready);
        Assert.Equal("unverified", body.CandidateMatch);
    }

    [Fact]
    public async Task Reload_reports_when_no_generation_was_previously_active()
    {
        var shell = WorkbenchShell.Development with
        {
            Settings = new Dictionary<string, string> { ["Elsa:Readiness:WarmDefaultShell"] = "false" }
        };
        await using var host = await WorkbenchProcess.StartAsync(shell, waitForReady: false);
        await WaitUntilWarmupDisabledAsync(host);
        var before = await ReadObservationAsync(host);
        Assert.Null(before.ActiveGeneration);
        Assert.False(before.Ready);

        using var response = await host.ManagementClient.PostAsync(ReloadPath, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReloadObservation>();
        Assert.NotNull(body);
        Assert.Equal("ready", body.Outcome);
        Assert.Null(body.PreviousGeneration);
        Assert.True(body.ReportedGeneration > 0);
        Assert.Equal(body.ReportedGeneration, body.ActiveGeneration);
        Assert.True(body.Ready);
        Assert.Equal("unverified", body.CandidateMatch);
    }

    [Fact]
    public async Task Failed_candidate_reports_the_retained_ready_generation_without_error_details()
    {
        await using var host = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);
        var before = await ReadObservationAsync(host);
        WorkbenchConfigurationFile.WriteOpenIddictSigningKey(Path.Combine(host.ContentRoot, "shells.json"), Canary);
        await Task.Delay(TimeSpan.FromMilliseconds(1200)); // Allow the JSON configuration provider to observe the replacement.

        using var response = await host.ManagementClient.PostAsync(ReloadPath, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<ReloadObservation>(rawBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(body);
        Assert.Equal("reload-failed", body.Outcome);
        Assert.Equal(before.ActiveGeneration, body.PreviousGeneration);
        Assert.Null(body.ReportedGeneration);
        Assert.Equal(before.ActiveGeneration, body.ActiveGeneration);
        Assert.True(body.Ready);
        Assert.Equal("unverified", body.CandidateMatch);

        Assert.DoesNotContain(Canary, rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error", rawBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_default_shell_observation_and_reload_routes_are_absent()
    {
        await using var host = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);

        using var observation = await host.ManagementClient.GetAsync("/_admin/composition/other-shell/observation");
        using var reload = await host.ManagementClient.PostAsync("/_admin/composition/other-shell/reload", null);

        Assert.Equal(HttpStatusCode.NotFound, observation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reload.StatusCode);
    }

    private static async Task<Observation> ReadObservationAsync(WorkbenchProcess host)
    {
        using var response = await host.ManagementClient.GetAsync(ObservationPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Observation>();
        Assert.NotNull(body);
        return body;
    }

    private static async Task<int?> ReadinessGenerationAsync(WorkbenchProcess host)
    {
        using var response = await host.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Readiness>();
        Assert.NotNull(body);
        return body.Generation;
    }

    private static async Task WaitUntilWarmupDisabledAsync(WorkbenchProcess host)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            using var response = await host.Client.GetAsync("/health/ready", timeout.Token);
            var body = await response.Content.ReadFromJsonAsync<JsonNode>(timeout.Token);
            if ((string?)body?["status"] == "disabled")
                return;

            await Task.Delay(50, timeout.Token);
        }
    }

    private sealed record Observation(string Shell, int? ActiveGeneration, bool Ready, string CandidateMatch);

    private sealed record ReloadObservation(
        string Outcome,
        int? PreviousGeneration,
        int? ReportedGeneration,
        int? ActiveGeneration,
        bool Ready,
        string CandidateMatch);

    private sealed record Readiness(int Generation);
}
