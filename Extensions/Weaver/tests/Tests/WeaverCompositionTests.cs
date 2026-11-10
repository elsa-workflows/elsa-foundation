using CShells;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Weaver.Anthropic;
using Elsa.Weaver.Api;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.GitHubCopilot;
using Elsa.Weaver.Services;
using Elsa.Weaver.Workflows;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

public sealed class WeaverCompositionTests
{
    private const string ShellName = "agent-composition";
    private const string DefaultFeatureId = "Weaver";

    private static readonly Type[] DefaultContracts =
    [
        typeof(IWeaverPolicyEvaluator),
        typeof(IWeaverContextSanitizer),
        typeof(IWeaverContextCollector),
        typeof(IWeaverCapabilityCatalog),
        typeof(IWeaverSessionService),
        typeof(IWeaverProposalService),
        typeof(IWeaverActionProposalExecutor),
        typeof(IWeaverAuditSink),
        typeof(IWeaverAuditReader),
        typeof(IWeaverFeedbackService),
        typeof(IWeaverToolRegistry),
        typeof(IWeaverTurnRegistry),
        typeof(IWeaverTurnStateStore),
        typeof(IWeaverToolInvoker),
        typeof(IWeaverStreamingService),
        typeof(IWeaverProviderRegistry),
        typeof(IWeaverSessionPolicyService)
    ];

    [Theory]
    [InlineData(typeof(WeaverApiFeature))]
    [InlineData(typeof(WeaverWorkflowsFeature))]
    [InlineData(typeof(WeaverGitHubCopilotFeature))]
    [InlineData(typeof(WeaverAnthropicFeature))]
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
        Assert.Equal(typeof(WeaverFeature), descriptor.StartupType);

        var enabled = services.GetRequiredService<ShellSettings>().EnabledFeatures.ToArray();
        Assert.Contains(DefaultFeatureId, enabled);
        Assert.Contains("Events", enabled);
        Assert.True(Array.IndexOf(enabled, "Events") < Array.IndexOf(enabled, DefaultFeatureId));
        Assert.True(Array.IndexOf(enabled, DefaultFeatureId) < Array.IndexOf(enabled, ShellBuilder.ResolveFeatureName(leaf)));

        if (leaf == typeof(WeaverApiFeature))
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
        await using var root = BuildRoot([typeof(WeaverWorkflowsFeature)], includeEvents: false);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        var missing = Assert.Single(ExceptionChain(failure).OfType<FeatureNotFoundException>());
        Assert.Equal("Events", missing.FeatureName);
        Assert.Equal(DefaultFeatureId, missing.DependentFeatureName);
    }

    [Theory]
    [InlineData(typeof(WeaverGitHubCopilotFeature))]
    [InlineData(typeof(WeaverAnthropicFeature))]
    public async Task Explicit_base_and_shared_leaves_keep_one_default_and_one_aggregator_per_event(Type providerFeature)
    {
        await using var root = BuildRoot(
            [typeof(WeaverApiFeature), typeof(WeaverWorkflowsFeature), providerFeature],
            includeBase: true);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;

        AssertSingleDefaults(services);
        Assert.Single(services.GetServices<IWeaverProvider>());
        Assert.NotNull(services.GetRequiredService<IWeaverProviderRegistry>().Active);
        AssertSingleContributionHandlers(services);
    }

    [Fact]
    public async Task Both_provider_leaves_preserve_single_defaults_and_refuse_the_provider_conflict()
    {
        await using var root = BuildRoot(
            [typeof(WeaverApiFeature), typeof(WeaverWorkflowsFeature),
                typeof(WeaverGitHubCopilotFeature), typeof(WeaverAnthropicFeature)],
            includeBase: true);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;

        // Provider alternatives cannot form one active harness. All independent generic defaults
        // still resolve once; the registry and its streaming consumer must report the real conflict.
        AssertSingleDefaults(services, hasProviderConflict: true);
        Assert.Equal(2, services.GetServices<IWeaverProvider>().Count());
        var failure = Assert.Throws<WeaverHarnessConflictException>(() =>
            services.GetRequiredService<IWeaverProviderRegistry>());
        Assert.Equal(2, failure.ProviderIds.Count);
        Assert.Throws<WeaverHarnessConflictException>(() => services.GetRequiredService<IWeaverStreamingService>());
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
        var registry = Assert.IsType<CountingTurnRegistry>(Assert.Single(services.GetServices<IWeaverTurnRegistry>()));
        var store = Assert.IsType<CountingTurnStateStore>(Assert.Single(services.GetServices<IWeaverTurnStateStore>()));
        var tool = services.GetRequiredService<ApplyChangeTool>();
        var policy = services.GetRequiredService<IWeaverSessionPolicyService>();
        Assert.IsType<DefaultWeaverSessionPolicyService>(policy);
        var sessions = services.GetRequiredService<IWeaverSessionService>();
        var streaming = services.GetRequiredService<IWeaverStreamingService>();
        var proposals = services.GetRequiredService<IWeaverProposalService>();
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", DeterministicWeaverProvider.Id, "explain", null,
            WeaverPolicy.Default, new Dictionary<string, string>()));
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, (await policy.GetAsync(session.Id, session.ActorId, session.TenantId)).Outcome);

        await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "mutate the resource", WeaverMessageStatus.Pending, null, [], []));
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, (await ChangeAsync(policy, session, WeaverAutonomyMode.FullAuto, 1)).Outcome);
        var first = await Collect(streaming.StreamAsync(session.Id));
        var proposal = Assert.IsType<WeaverActionProposal>(Assert.Single(first, x => x.Kind == WeaverStreamEventKind.ProposalCreated).Payload);
        // The replacements carried the lease and the retained pause; the owner's claims still govern policy.
        Assert.Equal((1, 1), (registry.Registers, registry.Unregisters));
        Assert.Equal(1, store.Saves);
        Assert.NotNull(await store.FindAsync(session.Id));
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, (await ChangeAsync(policy, session, WeaverAutonomyMode.FullAuto, 1)).Outcome);
        Assert.True((await proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
        Assert.Equal(WeaverSessionPolicyOutcome.Busy, (await ChangeAsync(policy, session, WeaverAutonomyMode.FullAuto, 1)).Outcome);

        Assert.Equal(WeaverStreamEventKind.Completed, (await Collect(streaming.StreamAsync(session.Id)))[^1].Kind);
        Assert.Null(await store.FindAsync(session.Id));
        Assert.Equal((2, 2), (registry.Registers, registry.Unregisters));
        var changed = await ChangeAsync(policy, session, WeaverAutonomyMode.FullAuto, 1);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, changed.Outcome);
        Assert.Equal(2, changed.State!.Revision);

        // The acknowledged FullAuto revision governs the next turn through the same replacements.
        await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "mutate the resource again", WeaverMessageStatus.Pending, null, [], []));
        var automatic = await Collect(streaming.StreamAsync(session.Id));
        Assert.Equal(WeaverStreamEventKind.Completed, automatic[^1].Kind);
        Assert.DoesNotContain(automatic, x => x.Kind == WeaverStreamEventKind.ProposalCreated);
        Assert.Equal(2, tool.Invocations);
        Assert.Equal(1, store.Saves);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, (await ChangeAsync(policy, session, WeaverAutonomyMode.Manual, 2)).Outcome);
    }

    [Theory]
    [InlineData(typeof(ForwardingSessionReplacementProbeFeature), typeof(IWeaverSessionService))]
    [InlineData(typeof(ForwardingOrchestratorReplacementProbeFeature), typeof(IWeaverStreamingService))]
    [InlineData(typeof(ForwardingInvokerReplacementProbeFeature), typeof(IWeaverToolInvoker))]
    [InlineData(typeof(ForwardingProposalReplacementProbeFeature), typeof(IWeaverProposalService))]
    [InlineData(typeof(CustomInvokerReplacementProbeFeature), typeof(IWeaverToolInvoker))]
    [InlineData(typeof(AdditiveInvokerProbeFeature), typeof(IWeaverToolInvoker))]
    public async Task A_non_participating_lifecycle_replacement_in_the_composed_shell_is_unsupported_without_shadow_state(Type feature, Type contract)
    {
        await using var root = BuildRoot([feature]);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;
        var resolved = services.GetRequiredService(contract);
        // The installed replacement is what every consumer resolves; no default participant stands in for it.
        Assert.IsNotAssignableFrom<IWeaverSessionPolicyParticipant>(resolved);
        Assert.Same(resolved, services.GetServices(contract).Last());
        Assert.Equal(feature == typeof(AdditiveInvokerProbeFeature) ? 2 : 1, services.GetServices(contract).Count());
        var policy = services.GetRequiredService<IWeaverSessionPolicyService>();
        var sessions = services.GetRequiredService<IWeaverSessionService>();
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", DeterministicWeaverProvider.Id, "explain", null,
            WeaverPolicy.Default, new Dictionary<string, string>()));

        // Existing behavior is otherwise unchanged: the prompt is accepted and its turn completes.
        await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "hello", WeaverMessageStatus.Pending, null, [], []));
        Assert.Equal(WeaverStreamEventKind.Completed, (await Collect(services.GetRequiredService<IWeaverStreamingService>().StreamAsync(session.Id)))[^1].Kind);

        var readback = await policy.GetAsync(session.Id, session.ActorId, session.TenantId);
        var refused = await ChangeAsync(policy, session, WeaverAutonomyMode.FullAuto, 1);
        Assert.Equal(WeaverSessionPolicyOutcome.Unsupported, readback.Outcome);
        Assert.Null(readback.State);
        Assert.Equal(WeaverSessionPolicyOutcome.Unsupported, refused.Outcome);
        Assert.Null(refused.State);
        var stored = (await sessions.FindAsync(session.Id))!;
        Assert.Equal(WeaverAutonomyMode.AutoReadOnly, stored.Policy.AutonomyMode);
        Assert.Equal(1, stored.PolicyRevision);
        // A new scope resolves the same refusal; nothing was recorded for a later supported read to expose.
        await using var later = shell.BeginScope();
        Assert.Equal(WeaverSessionPolicyOutcome.Unsupported,
            (await later.ServiceProvider.GetRequiredService<IWeaverSessionPolicyService>().GetAsync(session.Id, session.ActorId, session.TenantId)).Outcome);
    }

    // ---- #3441 T004: the record store and the audit trio are single replacement contracts ----

    [Fact]
    public async Task The_default_feature_registers_one_round_tripping_record_store_behind_the_owner()
    {
        await using var root = BuildRoot([typeof(WeaverApiFeature)]);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;

        var store = Assert.IsType<InMemoryWeaverSessionRecordStore>(Assert.Single(services.GetServices<IWeaverSessionRecordStore>()));
        Assert.True(store.SupportsFencing);
        var session = await services.GetRequiredService<IWeaverSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1",
            DeterministicWeaverProvider.Id, "explain", null, WeaverPolicy.Default, new Dictionary<string, string>()));
        var recorded = (await store.LoadAsync(session.Id))!;
        Assert.NotSame(session, recorded.Session);
        Assert.Equal(session.Id, recorded.Session.Id);
    }

    [Theory]
    [InlineData(typeof(DuplicateRecordStoreProbeFeature), typeof(IWeaverSessionRecordStore))]
    [InlineData(typeof(DuplicateAuditSinkProbeFeature), typeof(IWeaverAuditSink))]
    [InlineData(typeof(DuplicateAuditReaderProbeFeature), typeof(IWeaverAuditReader))]
    [InlineData(typeof(DuplicateAuditOwnershipProbeFeature), typeof(IWeaverAuditOwnership))]
    public async Task A_second_record_store_or_audit_registration_aborts_shell_activation_with_a_replacement_diagnostic(Type feature, Type contract)
    {
        await using var root = BuildRoot([feature]);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        var diagnostic = string.Join(" ", ExceptionChain(failure).Select(x => x.Message));
        Assert.Contains(contract.FullName!, diagnostic);
        Assert.Contains("at most one registration", diagnostic);
        Assert.Contains("services.SelectWeaver", diagnostic);
    }

    [Theory]
    [InlineData(typeof(IWeaverSessionRecordStore))]
    [InlineData(typeof(IWeaverAuditSink))]
    [InlineData(typeof(IWeaverAuditReader))]
    [InlineData(typeof(IWeaverAuditOwnership))]
    [InlineData(typeof(IWeaverRecoverySupport))]
    public void A_second_record_store_or_audit_registration_refuses_the_default_lifecycle_services(Type contract)
    {
        using var root = WeaverTestServices.Create(services => AddSecond(services, contract));
        using var scope = root.CreateScope();

        var failure = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IWeaverProposalService>());

        Assert.Contains(contract.FullName!, failure.Message);
        Assert.Contains("at most one registration", failure.Message);
    }

    [Fact]
    public async Task Replacing_the_record_store_and_the_audit_trio_keeps_one_of_each_and_a_supported_owner()
    {
        var store = new InMemoryWeaverSessionRecordStore();
        var audit = new InMemoryWeaverAuditStore();
        using var root = WeaverTestServices.Create(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IWeaverSessionRecordStore>(store));
            services.Replace(ServiceDescriptor.Singleton<IWeaverAuditSink>(audit));
            services.Replace(ServiceDescriptor.Singleton<IWeaverAuditReader>(audit));
            services.Replace(ServiceDescriptor.Singleton<IWeaverAuditOwnership>(audit));
        });
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;
        foreach (var initializer in services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(CancellationToken.None);

        var session = await services.GetRequiredService<IWeaverSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1",
            DeterministicWeaverProvider.Id, "explain", null, WeaverPolicy.Default, new Dictionary<string, string>()));

        Assert.NotNull(await store.LoadAsync(session.Id));
        Assert.Single(await audit.ListAsync(session.Id));
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded,
            (await services.GetRequiredService<IWeaverSessionPolicyService>().GetAsync(session.Id, session.ActorId, session.TenantId)).Outcome);
    }

    private static void AddSecond(IServiceCollection services, Type contract)
    {
        if (contract == typeof(IWeaverSessionRecordStore)) services.AddSingleton<IWeaverSessionRecordStore>(_ => new InMemoryWeaverSessionRecordStore());
        else if (contract == typeof(IWeaverAuditSink)) services.AddSingleton<IWeaverAuditSink>(_ => new InMemoryWeaverAuditStore());
        else if (contract == typeof(IWeaverAuditReader)) services.AddSingleton<IWeaverAuditReader>(_ => new InMemoryWeaverAuditStore());
        else if (contract == typeof(IWeaverRecoverySupport)) services.AddScoped<IWeaverRecoverySupport, DefaultWeaverRecoverySupport>();
        else services.AddSingleton<IWeaverAuditOwnership>(_ => new InMemoryWeaverAuditStore());
    }

    public abstract class DuplicateRegistrationProbeFeature(Type contract) : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) => AddSecond(services, contract);
    }

    [ShellFeature("AgentDuplicateRecordStoreProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class DuplicateRecordStoreProbeFeature() : DuplicateRegistrationProbeFeature(typeof(IWeaverSessionRecordStore));

    [ShellFeature("AgentDuplicateAuditSinkProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class DuplicateAuditSinkProbeFeature() : DuplicateRegistrationProbeFeature(typeof(IWeaverAuditSink));

    [ShellFeature("AgentDuplicateAuditReaderProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class DuplicateAuditReaderProbeFeature() : DuplicateRegistrationProbeFeature(typeof(IWeaverAuditReader));

    [ShellFeature("AgentDuplicateAuditOwnershipProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class DuplicateAuditOwnershipProbeFeature() : DuplicateRegistrationProbeFeature(typeof(IWeaverAuditOwnership));

    private static Task<WeaverSessionPolicyResult> ChangeAsync(IWeaverSessionPolicyService policy, WeaverSession session, WeaverAutonomyMode mode, long expectedRevision)
        => policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, mode, expectedRevision));

    private static async Task<List<WeaverStreamEvent>> Collect(IAsyncEnumerable<WeaverStreamEvent> events)
    {
        var result = new List<WeaverStreamEvent>();
        await foreach (var item in events)
            result.Add(item);
        return result;
    }

    /// <summary>A replacement probe enabled after the default feature; it replaces with the documented services.Replace(...).</summary>
    public abstract class LifecycleReplacementProbeFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer, AllowTestWeaverToolAuthorizer>());
            services.AddSingleton<IWeaverProvider, DeterministicWeaverProvider>();
            services.AddSingleton<ApplyChangeTool>();
            services.AddSingleton<IWeaverTool>(sp => sp.GetRequiredService<ApplyChangeTool>());
            WeaverTestServices.AddToolResultSource(services);
            Replace(services);
        }

        protected abstract void Replace(IServiceCollection services);
    }

    [ShellFeature("AgentParticipatingReplacementProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class ParticipatingReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton<IWeaverTurnRegistry>(_ => new CountingTurnRegistry(new WeaverTurnRegistry())));
            services.Replace(ServiceDescriptor.Singleton<IWeaverTurnStateStore>(_ => new CountingTurnStateStore(new InMemoryWeaverTurnStateStore())));
        }
    }

    [ShellFeature("AgentForwardingSessionReplacementProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class ForwardingSessionReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.Replace(ServiceDescriptor.Singleton<IWeaverSessionService>(sp =>
            new WeaverSessionPolicyTests.ForwardingSessions(new InMemoryWeaverSessionService(sp.GetRequiredService<IWeaverAuditSink>(), sp.GetRequiredService<TimeProvider>()))));
    }

    [ShellFeature("AgentForwardingOrchestratorReplacementProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class ForwardingOrchestratorReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.Replace(ServiceDescriptor.Scoped<IWeaverStreamingService>(sp =>
            new WeaverSessionPolicyTests.ForwardingStreaming(ActivatorUtilities.CreateInstance<DefaultWeaverTurnOrchestrator>(sp))));
    }

    [ShellFeature("AgentForwardingInvokerReplacementProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class ForwardingInvokerReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.Replace(ServiceDescriptor.Scoped<IWeaverToolInvoker>(sp =>
            new WeaverSessionPolicyTests.ForwardingInvoker(ActivatorUtilities.CreateInstance<DefaultWeaverToolInvoker>(sp))));
    }

    [ShellFeature("AgentForwardingProposalReplacementProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class ForwardingProposalReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.Replace(ServiceDescriptor.Scoped<IWeaverProposalService>(sp =>
            new WeaverSessionPolicyTests.ForwardingProposals(ActivatorUtilities.CreateInstance<InMemoryWeaverProposalService>(sp))));
    }

    [ShellFeature("AgentCustomInvokerReplacementProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class CustomInvokerReplacementProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.Replace(ServiceDescriptor.Scoped<IWeaverToolInvoker>(_ => new WeaverTestToolInvoker()));
    }

    /// <summary>An undocumented additive registration: the last registration wins everywhere, so it cannot participate either.</summary>
    [ShellFeature("AgentAdditiveInvokerProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    public sealed class AdditiveInvokerProbeFeature : LifecycleReplacementProbeFeature
    {
        protected override void Replace(IServiceCollection services) => services.AddScoped<IWeaverToolInvoker>(sp =>
            new WeaverSessionPolicyTests.ForwardingInvoker(ActivatorUtilities.CreateInstance<DefaultWeaverToolInvoker>(sp)));
    }

    internal sealed class CountingTurnRegistry(WeaverTurnRegistry inner) : IWeaverTurnRegistry
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

    internal sealed class CountingTurnStateStore(InMemoryWeaverTurnStateStore inner) : IWeaverTurnStateStore
    {
        private int _saves;
        public int Saves => Volatile.Read(ref _saves);

        public async Task SaveAsync(WeaverPausedTurn state, CancellationToken cancellationToken = default)
        {
            await inner.SaveAsync(state, cancellationToken);
            Interlocked.Increment(ref _saves);
        }

        public Task<WeaverPausedTurn?> FindAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindAsync(sessionId, cancellationToken);
        public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default) => inner.RemoveAsync(sessionId, cancellationToken);
    }

    private static void AssertSingleDefaults(IServiceProvider services, bool hasProviderConflict = false)
    {
        foreach (var contract in DefaultContracts)
        {
            if (hasProviderConflict && (contract == typeof(IWeaverProviderRegistry) || contract == typeof(IWeaverStreamingService)))
                continue;

            Assert.NotNull(Assert.Single(services.GetServices(contract)));
        }
    }

    private static void AssertSingleContributionHandlers(IServiceProvider services)
    {
        Assert.Single(services.GetServices<IEventHandler<WeaverContextCollecting>>());
        Assert.Single(services.GetServices<IEventHandler<WeaverCapabilitiesCollecting>>());
        Assert.Single(services.GetServices<IEventHandler<WeaverToolsCollecting>>());

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
                    shell.WithFeature<WeaverFeature>();
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
