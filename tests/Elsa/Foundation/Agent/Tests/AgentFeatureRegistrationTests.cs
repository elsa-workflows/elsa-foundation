using Elsa.Foundation.Agent.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Abstractions.Services;
using Elsa.Foundation.Agent.Api;
using Elsa.Foundation.Agent.GitHubCopilot;
using Elsa.Foundation.Agent.GitHubCopilot.Services;
using Elsa.Foundation.Workflows.Agent;
using Elsa.Foundation.Workflows.Agent.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Agent.Tests;

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
    public void Github_copilot_feature_registers_provider_facade()
    {
        var services = new ServiceCollection();

        new GitHubCopilotAgentFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var agentProvider = Assert.Single(provider.GetServices<IAgentProvider>());
        Assert.IsType<GitHubCopilotAgentProvider>(agentProvider);
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
        var session = new AgentSession(
            "session-1",
            "Test session",
            "tenant-1",
            "conversation-1",
            DeterministicAgentProvider.Id,
            "explain",
            AgentPolicy.Default,
            AgentSessionStatus.Active,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            new Dictionary<string, string>());

        var events = new List<AgentStreamEvent>();
        await foreach (var item in provider.SendMessageAsync(new(session.Id, "Hello", [])))
            events.Add(item);

        Assert.Collection(
            events,
            started => Assert.Equal(AgentStreamEventKind.Started, started.Kind),
            delta => Assert.Equal(AgentStreamEventKind.MessageDelta, delta.Kind),
            completed => Assert.Equal(AgentStreamEventKind.Completed, completed.Kind));
    }
}
