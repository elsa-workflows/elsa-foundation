using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// #2294: the optional Extension Builder of the built Workbench, run as an operator runs it. It is off unless
/// <c>Elsa:ExtensionBuilder:Enabled</c> is set, so a stock host has none of its routes. When it is on, its routes are mapped
/// on the host while the path-less shell resolves each request, and CShells copies every root registration into every
/// shell: a build enqueued on a shell's own copy of the queue would be read by no worker and stay running for ever
/// (#2159 is the same fault in Nuplane's trigger queue). So a build submitted over HTTP has to reach a terminal state.
/// </summary>
public sealed class WorkbenchExtensionBuilderTests
{
    private const string Api = "/_elsa/extension-builder";

    /// <summary>A build that cannot start fails within a second; one that outlasts this was never drained.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static readonly WorkbenchShell Enabled = WorkbenchShell.Development with
    {
        Settings = new Dictionary<string, string>
        {
            ["Elsa:ExtensionBuilder:Enabled"] = "true",
            // Under the process's own content root, so it is deleted with it.
            ["Elsa:ExtensionBuilder:StoragePath"] = "extension-builder",
            // No such executable: the build fails at once, with the worker having drained the queue and recorded the terminal state,
            // which is all this test needs. A real restore and pack would need network and minutes.
            ["Elsa:ExtensionBuilder:DotNetExecutable"] = "elsa-workbench-tests-no-such-dotnet"
        }
    };

    /// <summary>How the switch reaches a stock host: as appsettings.json commits it, or left out, or nulled, by an operator.</summary>
    public enum StockSwitch { AsCommitted, Removed, Null }

    /// <summary>Off unless set: a switch that is absent or null must not turn it on, as the parse before #1635 did.</summary>
    [Theory]
    [InlineData(StockSwitch.AsCommitted)]
    [InlineData(StockSwitch.Removed)]
    [InlineData(StockSwitch.Null)]
    public async Task A_stock_workbench_has_no_extension_builder_routes(StockSwitch stockSwitch)
    {
        string[] switchPath = ["Elsa", "ExtensionBuilder", "Enabled"];
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development, contentRoot =>
        {
            var appSettings = Path.Join(contentRoot, "appsettings.json");
            if (stockSwitch == StockSwitch.Removed)
                WorkbenchConfigurationFile.RemoveValue(appSettings, switchPath);
            else if (stockSwitch == StockSwitch.Null)
                WorkbenchConfigurationFile.WriteValue(appSettings, switchPath, null);
        });

        using var response = await workbench.ManagementClient.GetAsync($"{Api}/capabilities");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_enabled_workbench_serves_capabilities_to_the_management_key_only()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(Enabled);

        using var anonymous = await workbench.Client.GetAsync($"{Api}/capabilities");
        var capabilities = await workbench.ManagementClient.GetFromJsonAsync<JsonNode>($"{Api}/capabilities");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.True(capabilities!["canBuild"]!.GetValue<bool>(), capabilities.ToJsonString());
    }

    [Fact]
    public async Task A_build_submitted_over_http_is_drained_by_the_hosts_worker()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(Enabled);
        var workspace = await PostAsync(workbench, $"{Api}/workspaces", new { displayName = "Extension Builder smoke" });
        var project = await PostAsync(workbench, $"{Api}/workspaces/{workspace["id"]}/projects",
            new { templateId = "generic-dotnet", packageId = "Elsa.Workbench.Tests.ExtensionBuilderMarker", packageVersion = "1.0.0", targetFramework = "net10.0" });

        var submitted = await PostAsync(workbench, $"{Api}/projects/{project["id"]}/builds", content: null);
        var build = await TerminalBuildAsync(workbench, submitted["id"]!.GetValue<string>());

        Assert.Equal("failed", build["status"]!.GetValue<string>(), ignoreCase: true);
        Assert.Contains(build["diagnostics"]!.AsArray(), diagnostic => string.Equals(diagnostic!["severity"]!.GetValue<string>(), "error", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<JsonNode> PostAsync(WorkbenchProcess workbench, string path, object? content)
    {
        using var response = content is null
            ? await workbench.ManagementClient.PostAsync(path, content: null)
            : await workbench.ManagementClient.PostAsJsonAsync(path, content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"POST {path} answered {(int)response.StatusCode}: {body}. Host output:{Environment.NewLine}{workbench.Output}");
        return JsonNode.Parse(body)!;
    }

    private static async Task<JsonNode> TerminalBuildAsync(WorkbenchProcess workbench, string buildId)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (true)
        {
            var build = (await workbench.ManagementClient.GetFromJsonAsync<JsonNode>($"{Api}/builds/{buildId}"))!;
            // The default shell's JSON options decide the enum casing on the wire, so the terminal statuses are matched without regard to it.
            if (build["status"]!.GetValue<string>().ToLowerInvariant() is "succeeded" or "failed")
                return build;

            Assert.True(
                DateTimeOffset.UtcNow < deadline,
                $"Build {buildId} was still '{build["status"]}' after {Patience}: the host's worker never drained it. Host output:{Environment.NewLine}{workbench.Output}");
            await Task.Delay(250);
        }
    }
}
