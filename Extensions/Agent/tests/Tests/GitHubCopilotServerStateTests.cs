using Elsa.Agent.GitHubCopilot.Services;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Agent.Tests;

public sealed class GitHubCopilotServerStateTests
{
    [Fact]
    public void Tenant_paths_are_partitioned_without_interpreting_identifiers_as_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "elsa-copilot-state-test");
        var request = new GitHubCopilotClientRequest(null, false, null, null, root, null);
        var first = GitHubCopilotSdkClientFactory.StateDirectory(request with { TenantId = "../../tenant-a" });
        var second = GitHubCopilotSdkClientFactory.StateDirectory(request with { TenantId = "tenant-a" });
        var diagnostics = GitHubCopilotSdkClientFactory.StateDirectory(request);
        Assert.StartsWith(Path.Combine(root, "tenants") + Path.DirectorySeparatorChar, first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.NotEqual(second, diagnostics);
        Assert.NotEqual(GitHubCopilotSdkClientFactory.StateDirectory(request with { TenantId = "diagnostics" }), diagnostics);
        Assert.Equal(first, GitHubCopilotSdkClientFactory.StateDirectory(request with { TenantId = "../../tenant-a" }));
    }

    [Fact]
    public void Client_options_use_empty_mode_and_tagged_state_for_the_trusted_tenant()
    {
        var request = new GitHubCopilotClientRequest(null, false, null, null, Path.GetTempPath(), null) { TenantId = "tenant-a" };
        var options = new GitHubCopilotSdkClientFactory(NullLoggerFactory.Instance).BuildOptions(request);
        Assert.Equal(CopilotClientMode.Empty, options.Mode);
        Assert.Equal(GitHubCopilotSdkClientFactory.StateDirectory(request), options.BaseDirectory);
        Assert.Throws<ArgumentException>(() => GitHubCopilotSdkClientFactory.StateDirectory(request with { TenantId = " " }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_sdk_config_allows_only_current_registered_custom_callbacks_in_the_same_session(bool resume)
    {
        var request = Request();
        SessionConfigBase config = resume ? GitHubCopilotSdkClient.BuildResumeConfig(request) : GitHubCopilotSdkClient.BuildConfig(request);
        var owner = new PermissionInvocation { SessionId = request.SessionId };
        Assert.Equal("approve-once", (await config.OnPermissionRequest!(new PermissionRequestCustomTool { ToolDescription = "Fixture callback", ToolName = "allowed" }, owner)).Kind);
        Assert.Equal("reject", (await config.OnPermissionRequest(new PermissionRequestCustomTool { ToolDescription = "Fixture callback", ToolName = "excluded" }, owner)).Kind);
        Assert.Equal("reject", (await config.OnPermissionRequest(new PermissionRequestCustomTool { ToolDescription = "Fixture callback", ToolName = "not-allowlisted" }, owner)).Kind);
        Assert.Equal("reject", (await config.OnPermissionRequest(new PermissionRequestCustomTool { ToolDescription = "Fixture callback", ToolName = "not-installed" }, owner)).Kind);
        Assert.Equal("reject", (await config.OnPermissionRequest(new PermissionRequestCustomTool { ToolDescription = "Fixture callback", ToolName = "allowed" }, new() { SessionId = "foreign-session" })).Kind);
        Assert.Equal("reject", (await config.OnPermissionRequest(new PermissionRequestRead { Path = "private-file", Intention = "Read" }, owner)).Kind);
        Assert.Equal("reject", (await config.OnPermissionRequest(new PermissionRequestWrite { FileName = "private-file", Intention = "Write", Diff = "", CanOfferSessionApproval = false }, owner)).Kind);
        Assert.Equal("reject", (await config.OnPermissionRequest(new PermissionRequestShell { CanOfferSessionApproval = false, Commands = [], FullCommandText = "echo test", HasWriteFileRedirection = false, Intention = "Run", PossiblePaths = [], PossibleUrls = [] }, owner)).Kind);
        Assert.Equal("reject", (await config.OnPermissionRequest(new PermissionRequestMcp { ReadOnly = true, ServerName = "fixture", ToolName = "fixture", ToolTitle = "Fixture" }, owner)).Kind);
        Assert.Equal("reject", (await config.OnPermissionRequest(new PermissionRequestMemory { Fact = "Fixture" }, owner)).Kind);
        Assert.Equal("allowed", Assert.Single(config.Tools!).Name);
        var noTools = GitHubCopilotSdkClient.BuildResumeConfig(request with { AvailableTools = [] });
        Assert.Equal("reject", (await noTools.OnPermissionRequest!(new PermissionRequestCustomTool { ToolDescription = "Fixture callback", ToolName = "allowed" }, owner)).Kind);
        Assert.True(noTools.Tools is null || noTools.Tools.Count == 0);
    }

    [Fact]
    public async Task Sdk_callback_permission_never_bypasses_the_Elsa_invoker_refusal()
    {
        var calls = 0;
        var invoker = new AgentTestToolInvoker((_, _) =>
        {
            calls++;
            return Task.FromResult(AgentToolDispatch.Denied(new("native.denied", "Current native authority refused.", 403)));
        });
        var function = AgentToolAIFunctions.Create(new EchoTool(), new("session", "actor", [], AgentPolicy.Default, invoker)
            { TenantId = "tenant", TurnId = "turn" });
        var request = Request() with { AvailableTools = [function.Name], CustomTools = [function] };
        var config = GitHubCopilotSdkClient.BuildConfig(request);
        Assert.Equal("approve-once", (await config.OnPermissionRequest!(new PermissionRequestCustomTool { ToolDescription = "Fixture callback", ToolName = function.Name }, new() { SessionId = "session" })).Kind);
        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["text"] = "test" }));
        Assert.Contains("native.denied", System.Text.Json.JsonSerializer.Serialize(result)); Assert.Equal(1, calls);
    }

    private static GitHubCopilotSessionRequest Request() => new("session", null, null, true, null,
        ["allowed", "excluded", "not-installed"], ["excluded"],
        [AIFunctionFactory.Create((string text) => text, "allowed"),
         AIFunctionFactory.Create((string text) => text, "excluded"),
         AIFunctionFactory.Create((string text) => text, "not-allowlisted")]);
}
