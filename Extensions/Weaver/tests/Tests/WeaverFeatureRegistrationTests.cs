using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Extensions;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;
using Elsa.Weaver.GitHubCopilot;
using Elsa.Weaver.GitHubCopilot.Services;
using Elsa.Weaver.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

public sealed class WeaverFeatureRegistrationTests
{
    [Fact]
    public async Task Github_copilot_feature_registers_provider_facade()
    {
        using var provider = WeaverTestServices.Create(services => new WeaverGitHubCopilotFeature().ConfigureServices(services));
        using var scope = provider.CreateScope();
        var agentProvider = Assert.Single(scope.ServiceProvider.GetServices<IWeaverProvider>());
        Assert.IsType<WeaverGitHubCopilotProvider>(agentProvider);
        var diagnostics = await agentProvider.GetDiagnosticsAsync();
        Assert.Equal(WeaverProviderKind.ProviderSdkBinding, diagnostics.Kind);
        Assert.Equal(WeaverProviderRiskProfile.ReviewRequired, diagnostics.RiskProfile);
        Assert.False(diagnostics.IsAvailable);
        Assert.Contains(WeaverProviderOperation.Chat, diagnostics.SupportedOperations);
        Assert.Contains(WeaverProviderOperation.Streaming, diagnostics.SupportedOperations);
    }

    [Fact]
    public void Workflow_agent_feature_registers_capabilities_and_context()
    {
        using var root = WeaverTestServices.Create(services => new WeaverWorkflowsFeature().ConfigureServices(services));
        using var scope = root.CreateScope();
        var provider = scope.ServiceProvider;

        Assert.Contains(provider.GetServices<IWeaverCapabilityProvider>(), x => x.GetCapabilitiesAsync().AsTask().Result.Any(c => c.Id == "workflow.explain"));
        Assert.Contains(provider.GetServices<IWeaverContextProvider>(), x => x.ScopeKind == "workflow");
    }

    [Fact]
    public async Task Deterministic_provider_streams_contract_events_without_external_sdk()
    {
        var provider = new DeterministicWeaverProvider();
        var diagnostics = await provider.GetDiagnosticsAsync();
        var session = new WeaverSession(
            "session-1",
            "Test session",
            "tenant-1",
            "actor-1",
            "conversation-1",
            DeterministicWeaverProvider.Id,
            "explain",
            WeaverPolicy.Default,
            WeaverSessionStatus.Active,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            new Dictionary<string, string>());

        Assert.Equal(WeaverProviderKind.ProviderSdkBinding, diagnostics.Kind);
        Assert.Equal(WeaverProviderRiskProfile.ReadOnly, diagnostics.RiskProfile);
        Assert.Contains(WeaverProviderOperation.Chat, diagnostics.SupportedOperations);

        var events = new List<WeaverStreamEvent>();
        await foreach (var item in provider.ContinueTurnAsync(WeaverTurnContext.ForMessage(session.Id, "Hello", [])))
            events.Add(item);

        // The provider yields only content; the orchestrator owns turn lifecycle (Started/StepStarted/Completed).
        var delta = Assert.Single(events);
        Assert.Equal(WeaverStreamEventKind.MessageDelta, delta.Kind);
        Assert.Equal("Deterministic agent provider response.", delta.Content);
    }

    [Fact]
    public async Task Orchestrator_sends_latest_user_message_and_context_to_provider()
    {
        var provider = new CapturingWeaverProvider();
        var source = new WeaverTestContextSource("feature.fixture.context", "feature.fixture",
            [new("ctx-1", "feature.fixture", "resource-1", "Selected resource", "test.context", WeaverContextSensitivity.Internal,
                "selection", "Selected resource summary.", null, new Dictionary<string, string>()) { ModuleId = "feature.fixture" }]);
        using var sp = WeaverTestServices.Create(services =>
        {
            services.AddSingleton<IWeaverProvider>(provider);
            services.AddSingleton<IWeaverContextProvider>(source);
        });
        using var scope = sp.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IWeaverStreamingService>();
        var session = await sessions.CreateAsync(new(
            "tenant-1",
            "actor-1",
            "conversation-1",
            provider.ProviderId,
            "explain",
            "Test session",
            WeaverPolicy.Default,
            new Dictionary<string, string>()));
        var collected = await scope.ServiceProvider.GetRequiredService<IWeaverContextCollector>().CollectAsync(session.Policy,
            new(session.Id, source.ScopeKind, new Dictionary<string, string>()));
        Assert.True(collected.Succeeded, collected.Error?.Message);
        var context = collected.Value!.Attachments;
        await sessions.AddMessageAsync(session.Id, new(
            WeaverRole.User,
            "Explain this workflow.",
            WeaverMessageStatus.Pending,
            "workflow.explain",
            context.Select(item => item.Id).ToArray(),
            context));

        await foreach (var _ in streaming.StreamAsync(session.Id))
        {
        }

        Assert.NotNull(provider.LastMessage);
        Assert.Equal("Explain this workflow.", provider.LastMessage!.Content);
        Assert.Single(provider.LastMessage.Context);
    }

    private sealed class CapturingWeaverProvider : IWeaverProvider
    {
        public string ProviderId => "capturing-test";

        public WeaverProviderMessage? LastMessage { get; private set; }

        public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new WeaverProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));

        public async IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            LastMessage = new WeaverProviderMessage(context.SessionId, context.LatestUserMessage?.Content ?? string.Empty, context.Context);
            yield return new WeaverStreamEvent(Guid.NewGuid().ToString("N"), WeaverStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }

        public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new WeaverToolApprovalResult(request.Approved, string.Empty));

        public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WeaverProviderDiagnostics(
                ProviderId,
                true,
                "available",
                WeaverProviderKind.ProviderSdkBinding,
                [WeaverProviderOperation.Chat, WeaverProviderOperation.Streaming],
                WeaverProviderRiskProfile.ReadOnly,
                new Dictionary<string, string>()));
    }
}
