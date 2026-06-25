using Elsa.Agent.Api;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.GitHubCopilot;
using Elsa.Agent.GitHubCopilot.Services;
using Elsa.Agent.Workflows;
using Elsa.Agent.Workflows.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentFeatureRegistrationTests
{
    [Fact]
    public void Abstractions_feature_registers_backend_services()
    {
        var services = new ServiceCollection();

        new FoundationAgentAbstractionsFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAgentSessionService>());
        Assert.NotNull(provider.GetRequiredService<IAgentPolicyEvaluator>());
        Assert.NotNull(provider.GetRequiredService<IAgentContextSanitizer>());
        Assert.NotNull(provider.GetRequiredService<IAgentProposalService>());
        Assert.NotNull(provider.GetRequiredService<IAgentAuditReader>());
    }

    [Fact]
    public void Api_feature_registers_agent_abstractions()
    {
        var services = new ServiceCollection();

        new FoundationAgentApiFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAgentSessionService>());
    }

    [Fact]
    public async Task Github_copilot_feature_registers_provider_facade()
    {
        var services = new ServiceCollection();

        new GitHubCopilotAgentFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var agentProvider = Assert.Single(provider.GetServices<IAgentProvider>());
        Assert.IsType<GitHubCopilotAgentProvider>(agentProvider);
        var diagnostics = await agentProvider.GetDiagnosticsAsync();
        Assert.Equal(AgentProviderKind.ProviderSdkBinding, diagnostics.Kind);
        Assert.Equal(AgentProviderRiskProfile.ReviewRequired, diagnostics.RiskProfile);
        Assert.False(diagnostics.IsAvailable);
        Assert.Contains(AgentProviderOperation.Chat, diagnostics.SupportedOperations);
        Assert.Contains(AgentProviderOperation.Streaming, diagnostics.SupportedOperations);
    }

    [Fact]
    public void Workflow_agent_feature_registers_capabilities_and_context()
    {
        var services = new ServiceCollection();

        new FoundationWorkflowsAgentFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IWorkflowChangeProposalService>());
        Assert.Contains(provider.GetServices<IAgentCapabilityProvider>(), x => x.GetCapabilitiesAsync().AsTask().Result.Any(c => c.Id == "workflow.explain"));
        Assert.Contains(provider.GetServices<IAgentContextProvider>(), x => x.ScopeKind == "workflow");
    }

    [Fact]
    public async Task Deterministic_provider_streams_contract_events_without_external_sdk()
    {
        var provider = new DeterministicAgentProvider();
        var diagnostics = await provider.GetDiagnosticsAsync();
        var session = new AgentSession(
            "session-1",
            "Test session",
            "tenant-1",
            "actor-1",
            "conversation-1",
            DeterministicAgentProvider.Id,
            "explain",
            AgentPolicy.Default,
            AgentSessionStatus.Active,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            new Dictionary<string, string>());

        Assert.Equal(AgentProviderKind.ProviderSdkBinding, diagnostics.Kind);
        Assert.Equal(AgentProviderRiskProfile.ReadOnly, diagnostics.RiskProfile);
        Assert.Contains(AgentProviderOperation.Chat, diagnostics.SupportedOperations);

        var events = new List<AgentStreamEvent>();
        await foreach (var item in provider.SendMessageAsync(new(session.Id, "Hello", [])))
            events.Add(item);

        Assert.Collection(
            events,
            started => Assert.Equal(AgentStreamEventKind.Started, started.Kind),
            delta => Assert.Equal(AgentStreamEventKind.MessageDelta, delta.Kind),
            completed => Assert.Equal(AgentStreamEventKind.Completed, completed.Kind));
    }

    [Fact]
    public async Task Streaming_service_sends_latest_user_message_to_provider()
    {
        var audit = new InMemoryAgentAuditStore();
        var sessions = new InMemoryAgentSessionService(audit);
        var provider = new CapturingAgentProvider();
        var streaming = new DefaultAgentStreamingService(sessions, new DefaultAgentProviderRegistry([provider]));
        var session = await sessions.CreateAsync(new(
            "tenant-1",
            "actor-1",
            "conversation-1",
            provider.ProviderId,
            "explain",
            "Test session",
            AgentPolicy.Default,
            new Dictionary<string, string>()));
        await sessions.AddMessageAsync(session.Id, new(
            AgentRole.User,
            "Explain this workflow.",
            AgentMessageStatus.Pending,
            "workflow.explain",
            ["ctx-1"],
            [new("ctx-1", "workflow.definition", "wf-1", "Workflow", "workflow.definition", AgentContextSensitivity.Internal, "selection", "Workflow summary.", null, new Dictionary<string, string>())]));

        await foreach (var _ in streaming.StreamAsync(session.Id))
        {
        }

        Assert.NotNull(provider.LastMessage);
        Assert.Equal("Explain this workflow.", provider.LastMessage.Content);
        Assert.Single(provider.LastMessage.Context);
    }

    private sealed class CapturingAgentProvider : IAgentProvider
    {
        public string ProviderId => "capturing-test";

        public AgentProviderMessage? LastMessage { get; private set; }

        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));

        public async IAsyncEnumerable<AgentStreamEvent> SendMessageAsync(AgentProviderMessage message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            LastMessage = message;
            yield return new AgentStreamEvent(Guid.NewGuid().ToString("N"), AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(request.Approved, string.Empty));

        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(
                ProviderId,
                true,
                "available",
                AgentProviderKind.ProviderSdkBinding,
                [AgentProviderOperation.Chat, AgentProviderOperation.Streaming],
                AgentProviderRiskProfile.ReadOnly,
                new Dictionary<string, string>()));
    }
}
