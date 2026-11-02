using CShells;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Agent.Anthropic;
using Elsa.Agent.Api;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.GitHubCopilot;
using Elsa.Agent.Services;
using Elsa.Agent.Workflows;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

public sealed class AgentCompositionTests
{
    private const string ShellName = "agent-composition";
    private const string DefaultFeatureId = "FoundationAgentAbstractions";

    private static readonly Type[] DefaultContracts =
    [
        typeof(IAgentPolicyEvaluator),
        typeof(IAgentContextSanitizer),
        typeof(IAgentContextCollector),
        typeof(IAgentCapabilityCatalog),
        typeof(IAgentSessionService),
        typeof(IAgentProposalService),
        typeof(IAgentActionProposalExecutor),
        typeof(IAgentAuditSink),
        typeof(IAgentAuditReader),
        typeof(IAgentFeedbackService),
        typeof(IAgentToolRegistry),
        typeof(IAgentTurnRegistry),
        typeof(IAgentTurnStateStore),
        typeof(IAgentToolInvoker),
        typeof(IAgentStreamingService),
        typeof(IAgentProviderRegistry),
        typeof(IAgentSessionPolicyService)
    ];

    [Theory]
    [InlineData(typeof(FoundationAgentApiFeature))]
    [InlineData(typeof(FoundationWorkflowsAgentFeature))]
    [InlineData(typeof(GitHubCopilotAgentFeature))]
    [InlineData(typeof(AnthropicAgentFeature))]
    public async Task A_leaf_discovers_and_activates_its_default_feature_through_the_type_dependency(Type leaf)
    {
        // Only the leaf and Events assemblies are supplied. The default Agent assembly must be
        // discovered through the leaf's Type dependency, as must the API capability feature for
        // the API leaf. Dependencies are not explicitly enabled.
        await using var root = BuildRoot([leaf]);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;

        var descriptor = Assert.Single(services.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>(),
            feature => feature.Id == DefaultFeatureId);
        Assert.Equal(typeof(FoundationAgentAbstractionsFeature), descriptor.StartupType);

        var enabled = services.GetRequiredService<ShellSettings>().EnabledFeatures.ToArray();
        Assert.Contains(DefaultFeatureId, enabled);
        Assert.Contains("Events", enabled);
        Assert.True(Array.IndexOf(enabled, "Events") < Array.IndexOf(enabled, DefaultFeatureId));
        Assert.True(Array.IndexOf(enabled, DefaultFeatureId) < Array.IndexOf(enabled, ShellBuilder.ResolveFeatureName(leaf)));

        if (leaf == typeof(FoundationAgentApiFeature))
        {
            var capabilities = Assert.Single(services.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>(),
                feature => feature.Id == "ApiCapabilities");
            Assert.Equal(typeof(ApiCapabilitiesFeature), capabilities.StartupType);
            Assert.Contains("ApiCapabilities", enabled);
            Assert.True(Array.IndexOf(enabled, "ApiCapabilities") < Array.IndexOf(enabled, ShellBuilder.ResolveFeatureName(leaf)));
        }

        AssertSingleDefaults(services);
        Assert.NotNull(services.GetRequiredService<IInlineEventPublisher>());
    }

    [Fact]
    public async Task Missing_events_discovery_refuses_shell_activation_with_the_named_dependency()
    {
        await using var root = BuildRoot([typeof(FoundationWorkflowsAgentFeature)], includeEvents: false);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        var missing = Assert.Single(ExceptionChain(failure).OfType<FeatureNotFoundException>());
        Assert.Equal("Events", missing.FeatureName);
        Assert.Equal(DefaultFeatureId, missing.DependentFeatureName);
    }

    [Theory]
    [InlineData(typeof(GitHubCopilotAgentFeature))]
    [InlineData(typeof(AnthropicAgentFeature))]
    public async Task Explicit_base_and_shared_leaves_keep_one_default_and_one_aggregator_per_event(Type providerFeature)
    {
        await using var root = BuildRoot(
            [typeof(FoundationAgentApiFeature), typeof(FoundationWorkflowsAgentFeature), providerFeature],
            includeBase: true);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;

        AssertSingleDefaults(services);
        Assert.Single(services.GetServices<IAgentProvider>());
        Assert.NotNull(services.GetRequiredService<IAgentProviderRegistry>().Active);
        AssertSingleContributionHandlers(services);
    }

    [Fact]
    public async Task Both_provider_leaves_preserve_single_defaults_and_refuse_the_provider_conflict()
    {
        await using var root = BuildRoot(
            [typeof(FoundationAgentApiFeature), typeof(FoundationWorkflowsAgentFeature),
                typeof(GitHubCopilotAgentFeature), typeof(AnthropicAgentFeature)],
            includeBase: true);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;

        // Provider alternatives cannot form one active harness. All independent generic defaults
        // still resolve once; the registry and its streaming consumer must report the real conflict.
        AssertSingleDefaults(services, hasProviderConflict: true);
        Assert.Equal(2, services.GetServices<IAgentProvider>().Count());
        var failure = Assert.Throws<AgentHarnessConflictException>(() =>
            services.GetRequiredService<IAgentProviderRegistry>());
        Assert.Equal(2, failure.ProviderIds.Count);
        Assert.Throws<AgentHarnessConflictException>(() => services.GetRequiredService<IAgentStreamingService>());
        AssertSingleContributionHandlers(services);
    }

    // ---- #3440 T019: participating and unsupported lifecycle replacements in the package-composed shell ----

    [Fact]
    public async Task A_participating_registry_and_pause_store_replacement_keeps_the_composed_owner_supported_and_governing()
    {
        await using var root = BuildRoot([typeof(ParticipatingReplacementProbeFeature)]);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;
        var registry = Assert.IsType<CountingTurnRegistry>(Assert.Single(services.GetServices<IAgentTurnRegistry>()));
        var store = Assert.IsType<CountingTurnStateStore>(Assert.Single(services.GetServices<IAgentTurnStateStore>()));
        var tool = services.GetRequiredService<ApplyChangeTool>();
        var policy = services.GetRequiredService<IAgentSessionPolicyService>();
        Assert.IsType<DefaultAgentSessionPolicyService>(policy);
        var sessions = services.GetRequiredService<IAgentSessionService>();
        var streaming = services.GetRequiredService<IAgentStreamingService>();
        var proposals = services.GetRequiredService<IAgentProposalService>();
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", DeterministicAgentProvider.Id, "explain", null,
            AgentPolicy.Default, new Dictionary<string, string>()));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await policy.GetAsync(session.Id, session.ActorId, session.TenantId)).Outcome);

        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "mutate the resource", AgentMessageStatus.Pending, null, [], []));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await ChangeAsync(policy, session, AgentAutonomyMode.FullAuto, 1)).Outcome);
        var first = await Collect(streaming.StreamAsync(session.Id));
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, x => x.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        // The replacements carried the lease and the retained pause; the owner's claims still govern policy.
        Assert.Equal((1, 1), (registry.Registers, registry.Unregisters));
        Assert.Equal(1, store.Saves);
        Assert.NotNull(store.Find(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await ChangeAsync(policy, session, AgentAutonomyMode.FullAuto, 1)).Outcome);
        Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await ChangeAsync(policy, session, AgentAutonomyMode.FullAuto, 1)).Outcome);

        Assert.Equal(AgentStreamEventKind.Completed, (await Collect(streaming.StreamAsync(session.Id)))[^1].Kind);
        Assert.Null(store.Find(session.Id));
        Assert.Equal((2, 2), (registry.Registers, registry.Unregisters));
        var changed = await ChangeAsync(policy, session, AgentAutonomyMode.FullAuto, 1);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, changed.Outcome);
        Assert.Equal(2, changed.State!.Revision);

        // The acknowledged FullAuto revision governs the next turn through the same replacements.
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "mutate the resource again", AgentMessageStatus.Pending, null, [], []));
        var automatic = await Collect(streaming.StreamAsync(session.Id));
        Assert.Equal(AgentStreamEventKind.Completed, automatic[^1].Kind);
        Assert.DoesNotContain(automatic, x => x.Kind == AgentStreamEventKind.ProposalCreated);
        Assert.Equal(2, tool.Invocations);
        Assert.Equal(1, store.Saves);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await ChangeAsync(policy, session, AgentAutonomyMode.Manual, 2)).Outcome);
    }

    [Theory]
    [InlineData(typeof(ForwardingSessionReplacementProbeFeature), typeof(IAgentSessionService))]
    [InlineData(typeof(ForwardingOrchestratorReplacementProbeFeature), typeof(IAgentStreamingService))]
    [InlineData(typeof(ForwardingInvokerReplacementProbeFeature), typeof(IAgentToolInvoker))]
    [InlineData(typeof(ForwardingProposalReplacementProbeFeature), typeof(IAgentProposalService))]
    [InlineData(typeof(CustomInvokerReplacementProbeFeature), typeof(IAgentToolInvoker))]
    [InlineData(typeof(AdditiveInvokerProbeFeature), typeof(IAgentToolInvoker))]
    public async Task A_non_participating_lifecycle_replacement_in_the_composed_shell_is_unsupported_without_shadow_state(Type feature, Type contract)
    {
        await using var root = BuildRoot([feature]);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;
        var resolved = services.GetRequiredService(contract);
        // The installed replacement is what every consumer resolves; no default participant stands in for it.
        Assert.IsNotAssignableFrom<IAgentSessionPolicyParticipant>(resolved);
        Assert.Same(resolved, services.GetServices(contract).Last());
        Assert.Equal(feature == typeof(AdditiveInvokerProbeFeature) ? 2 : 1, services.GetServices(contract).Count());
        var policy = services.GetRequiredService<IAgentSessionPolicyService>();
        var sessions = services.GetRequiredService<IAgentSessionService>();
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", DeterministicAgentProvider.Id, "explain", null,
            AgentPolicy.Default, new Dictionary<string, string>()));

        // Existing behavior is otherwise unchanged: the prompt is accepted and its turn completes.
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "hello", AgentMessageStatus.Pending, null, [], []));
        Assert.Equal(AgentStreamEventKind.Completed, (await Collect(services.GetRequiredService<IAgentStreamingService>().StreamAsync(session.Id)))[^1].Kind);

        var readback = await policy.GetAsync(session.Id, session.ActorId, session.TenantId);
        var refused = await ChangeAsync(policy, session, AgentAutonomyMode.FullAuto, 1);
        Assert.Equal(AgentSessionPolicyOutcome.Unsupported, readback.Outcome);
        Assert.Null(readback.State);
        Assert.Equal(AgentSessionPolicyOutcome.Unsupported, refused.Outcome);
        Assert.Null(refused.State);
        var stored = (await sessions.FindAsync(session.Id))!;
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, stored.Policy.AutonomyMode);
        Assert.Equal(1, stored.PolicyRevision);
        // A new scope resolves the same refusal; nothing was recorded for a later supported read to expose.
        await using var later = shell.BeginScope();
        Assert.Equal(AgentSessionPolicyOutcome.Unsupported,
            (await later.ServiceProvider.GetRequiredService<IAgentSessionPolicyService>().GetAsync(session.Id, session.ActorId, session.TenantId)).Outcome);
    }

    private static Task<AgentSessionPolicyResult> ChangeAsync(IAgentSessionPolicyService policy, AgentSession session, AgentAutonomyMode mode, long expectedRevision)
        => policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, mode, expectedRevision));

    private static async Task<List<AgentStreamEvent>> Collect(IAsyncEnumerable<AgentStreamEvent> events)
    {
        var result = new List<AgentStreamEvent>();
        await foreach (var item in events)
            result.Add(item);
        return result;
    }

    /// <summary>A replacement probe enabled after the default feature; it replaces with the documented services.Replace(...).</summary>
    public abstract class LifecycleReplacementProbeFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer, AllowTestAgentToolAuthorizer>());
            services.AddSingleton<IAgentProvider, DeterministicAgentProvider>();
            services.AddSingleton<ApplyChangeTool>();
            services.AddSingleton<IAgentTool>(sp => sp.GetRequiredService<ApplyChangeTool>());
            AgentTestServices.AddToolResultSource(services);
            Replace(services);
        }

        protected abstract void Replace(IServiceCollection services);
    }

    [ShellFeature("AgentParticipatingReplacementProbe", DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) })]
    public sealed class ParticipatingReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton<IAgentTurnRegistry>(_ => new CountingTurnRegistry(new AgentTurnRegistry())));
            services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(_ => new CountingTurnStateStore(new InMemoryAgentTurnStateStore())));
        }
    }

    [ShellFeature("AgentForwardingSessionReplacementProbe", DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) })]
    public sealed class ForwardingSessionReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp =>
            new AgentSessionPolicyTests.ForwardingSessions(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>(), sp.GetRequiredService<TimeProvider>()))));
    }

    [ShellFeature("AgentForwardingOrchestratorReplacementProbe", DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) })]
    public sealed class ForwardingOrchestratorReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.Replace(ServiceDescriptor.Scoped<IAgentStreamingService>(sp =>
            new AgentSessionPolicyTests.ForwardingStreaming(ActivatorUtilities.CreateInstance<DefaultAgentTurnOrchestrator>(sp))));
    }

    [ShellFeature("AgentForwardingInvokerReplacementProbe", DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) })]
    public sealed class ForwardingInvokerReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.Replace(ServiceDescriptor.Scoped<IAgentToolInvoker>(sp =>
            new AgentSessionPolicyTests.ForwardingInvoker(ActivatorUtilities.CreateInstance<DefaultAgentToolInvoker>(sp))));
    }

    [ShellFeature("AgentForwardingProposalReplacementProbe", DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) })]
    public sealed class ForwardingProposalReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.Replace(ServiceDescriptor.Scoped<IAgentProposalService>(sp =>
            new AgentSessionPolicyTests.ForwardingProposals(ActivatorUtilities.CreateInstance<InMemoryAgentProposalService>(sp))));
    }

    [ShellFeature("AgentCustomInvokerReplacementProbe", DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) })]
    public sealed class CustomInvokerReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.Replace(ServiceDescriptor.Scoped<IAgentToolInvoker>(_ => new AgentTestToolInvoker()));
    }

    /// <summary>An undocumented additive registration: the last registration wins everywhere, so it cannot participate either.</summary>
    [ShellFeature("AgentAdditiveInvokerProbe", DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) })]
    public sealed class AdditiveInvokerProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.AddScoped<IAgentToolInvoker>(sp =>
            new AgentSessionPolicyTests.ForwardingInvoker(ActivatorUtilities.CreateInstance<DefaultAgentToolInvoker>(sp)));
    }

    internal sealed class CountingTurnRegistry(AgentTurnRegistry inner) : IAgentTurnRegistry
    {
        private int _registers;
        private int _unregisters;
        public int Registers => Volatile.Read(ref _registers);
        public int Unregisters => Volatile.Read(ref _unregisters);

        public CancellationToken Register(string sessionId, string turnId)
        {
            var token = inner.Register(sessionId, turnId);
            Interlocked.Increment(ref _registers);
            return token;
        }

        public bool Cancel(string sessionId, string turnId) => inner.Cancel(sessionId, turnId);

        public void Unregister(string sessionId, string turnId)
        {
            inner.Unregister(sessionId, turnId);
            Interlocked.Increment(ref _unregisters);
        }

        public bool IsActive(string sessionId) => inner.IsActive(sessionId);
    }

    internal sealed class CountingTurnStateStore(InMemoryAgentTurnStateStore inner) : IAgentTurnStateStore
    {
        private int _saves;
        public int Saves => Volatile.Read(ref _saves);

        public void Save(AgentPausedTurn state)
        {
            inner.Save(state);
            Interlocked.Increment(ref _saves);
        }

        public AgentPausedTurn? Find(string sessionId) => inner.Find(sessionId);
        public void Remove(string sessionId) => inner.Remove(sessionId);
    }

    private static void AssertSingleDefaults(IServiceProvider services, bool hasProviderConflict = false)
    {
        foreach (var contract in DefaultContracts)
        {
            if (hasProviderConflict && (contract == typeof(IAgentProviderRegistry) || contract == typeof(IAgentStreamingService)))
                continue;

            Assert.NotNull(Assert.Single(services.GetServices(contract)));
        }
    }

    private static void AssertSingleContributionHandlers(IServiceProvider services)
    {
        Assert.Single(services.GetServices<IEventHandler<AgentContextCollecting>>());
        Assert.Single(services.GetServices<IEventHandler<AgentCapabilitiesCollecting>>());
        Assert.Single(services.GetServices<IEventHandler<AgentToolsCollecting>>());

        var handlers = services.GetServices<IEventHandler>().Select(handler => handler.GetType()).ToArray();
        Assert.Equal(handlers.Length, handlers.Distinct().Count());
    }

    private static ServiceProvider BuildRoot(Type[] leaves, bool includeEvents = true, bool includeBase = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var assemblies = leaves.Select(feature => feature.Assembly).Distinct().ToList();
        if (includeEvents)
            assemblies.Add(typeof(EventsFeature).Assembly);

        services.AddCShells(builder => builder
            .WithAssemblies(assemblies.ToArray())
            .AddShell(ShellName, shell =>
            {
                if (includeBase)
                    shell.WithFeature<FoundationAgentAbstractionsFeature>();
                foreach (var leaf in leaves)
                    shell.WithFeature(leaf);
            }));
        return services.BuildServiceProvider();
    }

    private static IEnumerable<Exception> ExceptionChain(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
            yield return exception;
    }
}
