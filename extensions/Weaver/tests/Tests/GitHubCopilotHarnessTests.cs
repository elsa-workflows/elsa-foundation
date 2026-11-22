using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;
using Elsa.Weaver.GitHubCopilot.Options;
using Elsa.Weaver.GitHubCopilot.Services;
using Elsa.Weaver.Workflows.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Elsa.Weaver.Services;

namespace Elsa.Weaver.Tests;

public sealed class GitHubCopilotHarnessTests
{
    [Fact]
    public void Provider_is_a_loop_owning_harness_with_rich_capabilities()
    {
        var provider = BuildProvider(out _);

        Assert.IsAssignableFrom<IWeaverHarness>(provider);
        Assert.True(provider.Capabilities.HasFlag(WeaverHarnessCapabilities.Tools));
        Assert.True(provider.Capabilities.HasFlag(WeaverHarnessCapabilities.Plan));
        Assert.True(provider.Capabilities.HasFlag(WeaverHarnessCapabilities.Mcp));
    }

    [Fact]
    public async Task Harness_registers_tools_runs_them_and_maps_tool_execution_events()
    {
        var provider = BuildProvider(out var factory);
        var request = new WeaverHarnessTurnRequest("session-1", "actor-1", [new(WeaverRole.User, "do it")], [], [new EchoTool()], WeaverPolicy.Default)
        { TenantId = "tenant-1", TurnId = "turn-1", Client = new("client-1", "generation-1") };

        var events = new List<WeaverStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(request))
            events.Add(item);

        // Our tool was handed to the Copilot session as a config tool and auto-invoked by the (fake) harness.
        Assert.Single(factory.Session.RegisteredTools, tool => tool.Name == DeterministicWeaverProvider.ReadOnlyToolName);
        Assert.Equal(new[] { DeterministicWeaverProvider.ReadOnlyToolName }, factory.Session.LastRequest!.AvailableTools);
        Assert.DoesNotContain("read_file", factory.Session.LastRequest.AvailableTools);
        Assert.Contains("echo:hi", factory.Session.LastToolResult);
        Assert.Contains("test.echo", factory.Session.LastToolResult);
        var contributed = Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ContributedResult);
        Assert.Equal("test.echo", Assert.IsType<WeaverToolResult>(contributed.Payload).Payload?.TypeId);
        Assert.Equal("tenant-1", factory.LastInvocation?.TenantId);
        Assert.Equal("turn-1", factory.LastInvocation?.TurnId);
        Assert.Equal(request.Client, factory.LastInvocation?.Client);

        // The SDK's tool-execution events are mapped to our timeline vocabulary.
        var started = Assert.Single(events, x => x.Kind == WeaverStreamEventKind.ToolCallStarted);
        Assert.Equal(DeterministicWeaverProvider.ReadOnlyToolName, ((WeaverToolCallRequest)started.Payload!).ToolName);
        var completed = Assert.Single(events, x => x.Kind == WeaverStreamEventKind.ToolCallCompleted);
        Assert.True(((WeaverToolCallOutcome)completed.Payload!).Succeeded);
        Assert.Contains(events, x => x.Kind == WeaverStreamEventKind.MessageDelta && x.Content == "done");
    }

    [Theory]
    [InlineData("error")]
    [InlineData("custom-transport")]
    [InlineData("ambient-transport")]
    public async Task Harness_normalizes_sdk_error_and_transport_paths_before_exposing_generic_events(string path)
    {
        var provider = BuildProvider(out var factory);
        factory.Options!.RuntimeConnectionToken = "runtime-fixture-secret";
        const string message = "SDK failed with test-token runtime-fixture-secret";
        if (path == "error") factory.Session.ExtraEvents.Add(new(GitHubCopilotStreamEventKind.Error, ErrorCode: "sdk.error", ErrorMessage: message));
        else
        {
            factory.Session.ExtraEvents.Add(new(GitHubCopilotStreamEventKind.ToolExecutionStarted, ToolCallId: "failed-call", ToolName: path == "custom-transport" ? "echo" : "ambient-tool"));
            factory.Session.ExtraEvents.Add(new(GitHubCopilotStreamEventKind.ToolExecutionCompleted, ToolCallId: "failed-call", Success: false, ErrorMessage: message));
        }
        var request = new WeaverHarnessTurnRequest("session-1", "actor-1", [new(WeaverRole.User, "Read")], [], [new EchoTool()], WeaverPolicy.Default)
            { TenantId = "tenant-1", TurnId = "turn" };
        var events = new List<WeaverStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(request)) events.Add(item);
        var serialized = JsonSerializer.Serialize(events);
        Assert.Contains("[redacted]", serialized);
        Assert.DoesNotContain("test-token", serialized); Assert.DoesNotContain("runtime-fixture-secret", serialized);
    }

    [Fact]
    public async Task Harness_requires_a_trusted_tenant_before_creating_a_client()
    {
        var provider = BuildProvider(out var factory);
        var request = new WeaverHarnessTurnRequest("session-1", "actor-1", [new(WeaverRole.User, "Read")], [], [], WeaverPolicy.Default);
        var events = new List<WeaverStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(request)) events.Add(item);
        Assert.Equal("weaver.provider.github_copilot.session_owner_unavailable", Assert.Single(events).Error!.Code);
        Assert.Null(factory.Session.LastRequest);
    }

    [Fact]
    public async Task Harness_runs_the_workflow_read_graph_tool_and_feeds_the_graph_back()
    {
        var provider = BuildProvider(out var factory, new WorkflowReadGraphTool());
        var graph = WorkflowWeaverTests.Snapshot();
        var request = new WeaverHarnessTurnRequest("session-1", "actor-1", [new(WeaverRole.User, "read the graph")], [graph], [new WorkflowReadGraphTool()], WeaverPolicy.Default) { TenantId = "tenant-1" };

        var events = new List<WeaverStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(request))
            events.Add(item);

        // The real read-only workflow tool ran and its serialized graph (real node ids) was fed back to the SDK.
        Assert.Contains("node-1", factory.Session.LastToolResult);
        Assert.Contains("workflows.graph", factory.Session.LastToolResult);
        var started = Assert.Single(events, x => x.Kind == WeaverStreamEventKind.ToolCallStarted);
        Assert.Equal(WorkflowReadGraphTool.ToolName, ((WeaverToolCallRequest)started.Payload!).ToolName);
        Assert.Contains(events, x => x.Kind == WeaverStreamEventKind.ToolCallCompleted);
    }

    [Fact]
    public async Task Harness_surfaces_a_proposal_when_an_auto_invoked_tool_is_mutating()
    {
        var provider = BuildProvider(out var factory, new ApplyChangeTool());
        var request = new WeaverHarnessTurnRequest("session-1", "actor-1", [new(WeaverRole.User, "apply a change")], [], [new ApplyChangeTool()], WeaverPolicy.Default) { TenantId = "tenant-1" };

        var events = new List<WeaverStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(request))
            events.Add(item);

        // Under the default (review) policy a mutating tool becomes a proposal instead of running inline.
        Assert.Contains(events, x => x.Kind == WeaverStreamEventKind.ProposalCreated);
        Assert.Contains("awaiting human approval", factory.Session.LastToolResult);
    }

    [Fact]
    public async Task Receipt_continuation_preserves_typed_result_without_replaying_request_or_advertising_mutation()
    {
        var provider = BuildProvider(out var factory, new EchoTool(), new ApplyChangeTool());
        var result = new WeaverToolResult("prior-call", "prior-tool", true, "Owning module observed the change.",
            new("independent.result", "7", JsonSerializer.SerializeToElement(new { actualValue = 42 })));
        var request = new WeaverHarnessTurnRequest("session-1", "actor-1", [new(WeaverRole.User, "ORIGINAL_MUTATION_PROMPT_DO_NOT_REPLAY")], [],
            [new EchoTool(), new ApplyChangeTool()], WeaverPolicy.Default)
        { IsReceiptContinuation = true, PendingToolResults = [result], TurnId = "turn-2", TenantId = "tenant-1" };
        await foreach (var _ in provider.RunTurnAsync(request)) { }
        Assert.DoesNotContain("ORIGINAL_MUTATION_PROMPT_DO_NOT_REPLAY", factory.Session.LastPrompt);
        Assert.Contains("independent.result", factory.Session.LastPrompt);
        Assert.Contains("42", factory.Session.LastPrompt);
        Assert.Empty(factory.Session.RegisteredTools);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_envelope_identity_and_provenance_reach_the_provider(bool changeOrigin)
    {
        var attachment = IndependentContext();
        var changed = changeOrigin
            ? attachment with { Origin = WeaverContextOrigin.AuthoritativeRead }
            : attachment with { Target = attachment.Target! with { ResourceId = "other-resource", Revision = "other-revision" } };

        var originalPrompt = await ContextPrompt([attachment]);
        var changedPrompt = await ContextPrompt([changed]);

        Assert.NotEqual(originalPrompt, changedPrompt);
        Assert.Contains("independent.module", originalPrompt);
        Assert.Contains("\"schemaVersion\":\"7\"", originalPrompt);
        Assert.Contains("envelope-only-resource", originalPrompt);
        Assert.Contains("envelope-only-revision", originalPrompt);
        Assert.Contains("ClientSnapshot", originalPrompt);
        Assert.Contains(changeOrigin ? "AuthoritativeRead" : "other-resource", changedPrompt);
        Assert.DoesNotContain("private-client-binding", originalPrompt);
        Assert.DoesNotContain("private-authority-scope", originalPrompt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_metadata_preserves_the_sensitive_content_gate(bool includeSensitive)
    {
        var sensitive = IndependentContext() with { Sensitivity = WeaverContextSensitivity.Sensitive, Content = new { value = "SENSITIVE_CONTENT_ONLY" } };
        var secret = IndependentContext() with { Id = "secret", Sensitivity = WeaverContextSensitivity.Secret, Content = new { value = "SECRET_CONTENT_ONLY" } };

        var prompt = await ContextPrompt([sensitive, secret], includeSensitive);

        Assert.Contains("envelope-only-resource", prompt);
        Assert.Equal(includeSensitive, prompt.Contains("SENSITIVE_CONTENT_ONLY", StringComparison.Ordinal));
        Assert.DoesNotContain("SECRET_CONTENT_ONLY", prompt);
        Assert.DoesNotContain("private-client-binding", prompt);
        Assert.DoesNotContain("private-authority-scope", prompt);
    }

    [Fact]
    public async Task Context_metadata_keeps_resource_data_distinct_from_installed_guidance()
    {
        var resource = IndependentContext() with { Label = "Untrusted resource", Content = new { instructions = "UNTRUSTED_RESOURCE_INSTRUCTION" } };
        var guidance = IndependentContext() with { Id = "guidance", Label = "Trusted module", Target = null, Origin = WeaverContextOrigin.InstalledGuidance, Content = new { instructions = "INSTALLED_MODULE_GUIDANCE" } };

        var prompt = await ContextPrompt([resource, guidance]);

        var resourceStart = prompt.IndexOf("Resource data: Untrusted resource", StringComparison.Ordinal);
        var guidanceStart = prompt.IndexOf("Installed module guidance: Trusted module", StringComparison.Ordinal);
        Assert.True(resourceStart >= 0 && guidanceStart > resourceStart);
        var resourceSection = prompt[resourceStart..guidanceStart];
        var guidanceSection = prompt[guidanceStart..];
        Assert.Contains("UNTRUSTED_RESOURCE_INSTRUCTION", resourceSection);
        Assert.Contains("ClientSnapshot", resourceSection);
        Assert.DoesNotContain("INSTALLED_MODULE_GUIDANCE", resourceSection);
        Assert.Contains("INSTALLED_MODULE_GUIDANCE", guidanceSection);
        Assert.Contains("InstalledGuidance", guidanceSection);
        Assert.DoesNotContain("UNTRUSTED_RESOURCE_INSTRUCTION", guidanceSection);
        Assert.DoesNotContain("envelope-only-resource", guidanceSection);
    }

    private static WeaverContextAttachment IndependentContext() => new("context", "independent.context", null, "Resource", "independent.context",
        WeaverContextSensitivity.Internal, "selection", "Bounded resource data.", new { value = "same-domain-content" }, new Dictionary<string, string>())
    {
        ModuleId = "independent.module", SchemaVersion = "7", Origin = WeaverContextOrigin.ClientSnapshot,
        Target = new("independent.module", "independent.resource", "envelope-only-resource", "envelope-only-revision"),
        Client = new("private-client-binding", "private-generation"), WeaverScopes = ["private-authority-scope"]
    };

    private static async Task<string> ContextPrompt(IReadOnlyCollection<WeaverContextAttachment> attachments, bool includeSensitiveContextContent = false)
    {
        var provider = BuildProvider(out var factory, includeSensitiveContextContent);
        var request = new WeaverHarnessTurnRequest("session-1", "actor-1", [new(WeaverRole.User, "Inspect")], attachments, [], WeaverPolicy.Default) { TenantId = "tenant-1" };
        await foreach (var _ in provider.RunTurnAsync(request)) { }
        return Assert.IsType<string>(factory.Session.LastPrompt);
    }

    private static WeaverGitHubCopilotProvider BuildProvider(out FakeCopilotClientFactory factory, params IWeaverTool[] tools)
        => BuildProvider(out factory, false, tools);

    private static WeaverGitHubCopilotProvider BuildProvider(out FakeCopilotClientFactory factory, bool includeSensitiveContextContent, params IWeaverTool[] tools)
    {
        factory = new FakeCopilotClientFactory();
        var options = new WeaverGitHubCopilotOptions { Enabled = true, GitHubToken = "test-token", IncludeSensitiveContextContent = includeSensitiveContextContent };
        options.GitHubTokenEnvironmentVariable = "ELSA_COPILOT_TEST_TOKEN_NOT_SET";
        factory.Options = options;
        IWeaverTool[] registered = tools.Length == 0 ? [new EchoTool()] : tools;
        var capturedFactory = factory;
        var invoker = new WeaverTestToolInvoker(async (invocation, policy) =>
        {
            capturedFactory.LastInvocation = invocation;
            var tool = registered.Single(item => item.Descriptor.Name == invocation.ToolName);
            var preparation = await tool.PrepareAsync(invocation);
            if (!preparation.Succeeded) return WeaverToolDispatch.Denied(preparation.Error!);
            var prepared = WeaverTestToolHelpers.Prepared(invocation, tool.Descriptor, preparation.Value!);
            if (tool.Descriptor.Mutability == WeaverToolMutability.Mutating && policy.RequiresApprovalForMutations)
            {
                var now = DateTimeOffset.UtcNow;
                return WeaverToolDispatch.Proposed(new("proposal-1", invocation.SessionId, null, tool.Descriptor.Name, "tool-invocation",
                    tool.Descriptor.DisplayName, tool.Descriptor.Description, tool.Descriptor.Risk, prepared.Target?.Revision,
                    [], [], [], null, [], prepared.Target?.ResourceType, prepared.Target?.ResourceId, true,
                    WeaverActionProposalStatus.AwaitingApproval, null, null, now, now) { PreparedInvocation = prepared });
            }
            var result = await tool.ExecuteAsync(prepared);
            return result.Succeeded ? WeaverToolDispatch.Executed(result.Value!) : WeaverToolDispatch.Denied(result.Error!);
        });
        return new WeaverGitHubCopilotProvider(factory, Options.Create(options), invoker, NullLogger<WeaverGitHubCopilotProvider>.Instance);
    }

    private sealed class FakeCopilotClientFactory : IGitHubCopilotClientFactory
    {
        public WeaverGitHubCopilotOptions? Options { get; set; }
        public FakeCopilotSession Session { get; } = new();
        public WeaverToolInvocation? LastInvocation { get; set; }

        public IGitHubCopilotClient Create(GitHubCopilotClientRequest request) => new FakeCopilotClient(Session);
    }

    private sealed class FakeCopilotClient(FakeCopilotSession session) : IGitHubCopilotClient
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        {
            session.LastRequest = request;
            session.Configure(request.CustomTools);
            return Task.FromResult<IGitHubCopilotSession>(session);
        }

        public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        {
            session.LastRequest = request;
            session.Configure(request.CustomTools);
            return Task.FromResult<IGitHubCopilotSession>(session);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCopilotSession : IGitHubCopilotSession
    {
        public GitHubCopilotSessionRequest? LastRequest { get; set; }
        public List<GitHubCopilotStreamEvent> ExtraEvents { get; } = [];
        private IReadOnlyCollection<AIFunction> _tools = [];

        public string SessionId => "session-1";
        public string? WorkspacePath => null;
        public IReadOnlyCollection<AIFunction> RegisteredTools => _tools;
        public string? LastToolResult { get; private set; }
        public string? LastPrompt { get; private set; }

        public void Configure(IReadOnlyCollection<AIFunction>? tools) => _tools = tools ?? [];

        // Simulates the Copilot harness auto-invoking a config tool and emitting its tool-execution lifecycle.
        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            yield return new(GitHubCopilotStreamEventKind.Started);

            var tool = _tools.FirstOrDefault();
            if (tool is not null)
            {
                yield return new(GitHubCopilotStreamEventKind.ToolExecutionStarted, ToolCallId: "call-1", ToolName: tool.Name);
                var result = await tool.InvokeAsync(new AIFunctionArguments(tool.Name == WorkflowReadGraphTool.ToolName ? new Dictionary<string, object?>() : new Dictionary<string, object?> { ["text"] = "hi" }), cancellationToken);
                LastToolResult = JsonSerializer.Serialize(result);
                yield return new(GitHubCopilotStreamEventKind.ToolExecutionCompleted, ToolCallId: "call-1", Success: true);
            }

            foreach (var item in ExtraEvents) yield return item;

            yield return new(GitHubCopilotStreamEventKind.MessageDelta, "done");
            yield return new(GitHubCopilotStreamEventKind.Completed);
        }

        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
