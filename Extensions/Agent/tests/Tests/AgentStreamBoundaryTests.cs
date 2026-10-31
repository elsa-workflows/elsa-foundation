using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Tests;

public sealed partial class AgentCumulativeDisclosureTests
{
    public static IEnumerable<object[]> ReceiptBoundaryCases()
    {
        foreach (var asynchronous in new[] { false, true })
        foreach (var api in new[] { false, true })
        foreach (var fails in new[] { false, true })
            yield return [asynchronous, api, fails];
    }

    [Theory]
    [MemberData(nameof(ReceiptBoundaryCases))]
    public async Task R9_receipt_resume_preserves_store_cause_pause_and_exact_receipt_then_recovers_without_replay(bool asynchronous, bool api, bool fails)
    {
        var failure = new IOException("Private receipt insert cause " + Guid.NewGuid().ToString("N"));
        var logger = new RecordingLogger();
        using var fixture = new Fixture(true, Script.PauseThenAnswer, services => AddBoundaryStores(services, logger));
        fixture.CaptureDomainFailures = true;
        await fixture.Post("Apply one reviewed owner change");
        var initial = await fixture.Stream();
        var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(initial, item => item.Kind == AgentStreamEventKind.ProposalCreated).Payload);
        var proposals = fixture.Services.GetRequiredService<IAgentProposalService>();
        Assert.True((await proposals.ApproveAsync(proposal.Id, fixture.Session!.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await proposals.ExecuteAsync(proposal.Id, fixture.Session.ActorId, proposal.BaseRevision)).Succeeded);
        var accepted = await proposals.FindResultAsync(proposal.Id);
        var pause = fixture.Services.GetRequiredService<PauseBoundary>().Find(fixture.Session.Id);
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        store.AsyncFailures = asynchronous;
        if (fails) store.ThrowBeforeToolInsert = failure;

        var events = new List<AgentStreamEvent>();
        var wire = api ? await fixture.StreamViaApi() : null;
        if (!api) events = await fixture.Stream();

        if (fails)
        {
            var domain = api
                ? Assert.IsType<AgentContributionException>(Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error).Exception)
                : Assert.Single(fixture.DomainFailures);
            Assert.Contains(ExceptionChain(domain), cause => ReferenceEquals(cause, failure));
            Assert.DoesNotContain(failure.Message, domain.Message, StringComparison.Ordinal);
            if (api) Assert.DoesNotContain(failure.Message, wire, StringComparison.Ordinal);
            Assert.Single(fixture.Probe.Inputs);
            Assert.Equal(pause, fixture.Services.GetRequiredService<PauseBoundary>().Find(fixture.Session.Id));
            Assert.Equal(accepted, await proposals.FindResultAsync(proposal.Id));
            Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session.Id));
            Assert.Contains(await fixture.Stream(), item => item.Kind == AgentStreamEventKind.Completed);
        }
        else if (api) Assert.Contains("completed", wire, StringComparison.Ordinal);
        else Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Completed);
        Assert.Equal(2, fixture.Probe.Inputs.Count);
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
        Assert.Equal(accepted, await proposals.FindResultAsync(proposal.Id));
    }

    public static IEnumerable<object[]> PrimaryBoundaryCases()
    {
        foreach (var boundary in new[] { "streaming", "catalog" })
        foreach (var asynchronous in new[] { false, true })
        foreach (var api in new[] { false, true })
        foreach (var fails in new[] { false, true })
            yield return [boundary, asynchronous, api, fails];
    }

    [Theory]
    [MemberData(nameof(PrimaryBoundaryCases))]
    public async Task R9_synchronous_and_async_primary_boundaries_survive_cleanup_and_remain_redacted(string boundary, bool asynchronous, bool api, bool fails)
    {
        var primary = new IOException("Private primary boundary cause " + Guid.NewGuid().ToString("N"));
        var secondary = new IOException("Private terminal cleanup cause " + Guid.NewGuid().ToString("N"));
        var declared = new AgentContributionException("Fixture boundary failed safely.", primary);
        var logger = new RecordingLogger();
        var catalog = new CatalogFault { Asynchronous = asynchronous };
        using var fixture = new Fixture(true, Script.Answer, services =>
        {
            AddBoundaryStores(services, logger);
            services.AddScoped<DefaultAgentToolRegistry>();
            services.Replace(ServiceDescriptor.Scoped<IAgentToolRegistry>(sp => new BoundaryCatalog(sp.GetRequiredService<DefaultAgentToolRegistry>(), catalog)));
        });
        fixture.CaptureDomainFailures = true;
        await fixture.Post("Do not lose the original failure during cleanup");
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        store.AsyncFailures = asynchronous;
        if (fails)
        {
            if (boundary == "streaming") store.ThrowOnStreamingStatus = declared;
            else catalog.Failure = declared;
            store.ThrowOnTerminalStatus = secondary;
        }

        var wire = api ? await fixture.StreamViaApi() : null;
        List<AgentStreamEvent> events = api ? [] : await fixture.Stream();

        if (fails)
        {
            var domain = api
                ? Assert.IsType<AgentContributionException>(Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error).Exception)
                : Assert.Single(fixture.DomainFailures);
            var chain = ExceptionChain(domain).ToArray();
            Assert.Contains(chain, cause => ReferenceEquals(cause, primary));
            Assert.Contains(chain, cause => ReferenceEquals(cause, secondary));
            Assert.Empty(fixture.Probe.Inputs);
            Assert.Equal(1, fixture.Services.GetRequiredService<PauseBoundary>().RemoveCalls);
            Assert.Equal(1, store.TerminalStatusCalls);
            if (api) Assert.DoesNotContain("Private", wire, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(fixture.DomainFailures);
            Assert.Single(fixture.Probe.Inputs);
            if (!api) Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Completed);
        }
        Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session!.Id));
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData("find", false)]
    [InlineData("find", true)]
    [InlineData("list", false)]
    [InlineData("list", true)]
    [InlineData("pause", false)]
    [InlineData("register", false)]
    [InlineData("active", false)]
    public async Task R9_initial_store_registration_and_catalog_boundaries_wrap_original_causes(string boundary, bool asynchronous)
    {
        var failure = new IOException("Private initial boundary cause " + Guid.NewGuid().ToString("N"));
        var registry = new RegistrationFault();
        var providerRegistry = new ProviderRegistryFault();
        using var fixture = new Fixture(false, Script.Answer, services =>
        {
            AddBoundaryStores(services, new RecordingLogger());
            services.Replace(ServiceDescriptor.Singleton<IAgentTurnRegistry>(registry));
            var original = Assert.IsAssignableFrom<IAgentProvider>(Assert.Single(services, d => d.ServiceType == typeof(IAgentProvider)).ImplementationInstance);
            providerRegistry.Provider = original;
            services.Replace(ServiceDescriptor.Scoped<IAgentProviderRegistry>(_ => providerRegistry));
        });
        fixture.CaptureDomainFailures = true;
        await fixture.Post("Fail before provider input");
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        store.AsyncFailures = asynchronous;
        if (boundary == "find") store.ThrowOnFind = failure;
        if (boundary == "list") store.ThrowOnList = failure;
        if (boundary == "pause") fixture.Services.GetRequiredService<PauseBoundary>().ThrowOnFind = failure;
        if (boundary == "register") registry.Failure = failure;
        if (boundary == "active") providerRegistry.Failure = failure;

        await fixture.Stream();

        var domain = Assert.Single(fixture.DomainFailures);
        Assert.Contains(ExceptionChain(domain), cause => ReferenceEquals(cause, failure));
        Assert.Empty(fixture.Probe.Inputs);
        Assert.False(registry.IsActive(fixture.Session!.Id));
    }

    public static IEnumerable<object[]> CheckpointAuthorityCases()
    {
        foreach (var phase in new[] { "initial", "callback", "result", "receipt" })
        foreach (var delegated in new[] { false, true })
        foreach (var withdraw in new[] { false, true })
            yield return [phase, delegated, withdraw, false];
        foreach (var phase in new[] { "initial", "result" })
        foreach (var delegated in new[] { false, true })
        foreach (var withdraw in new[] { false, true })
            yield return [phase, delegated, withdraw, true];
    }

    [Theory]
    [MemberData(nameof(CheckpointAuthorityCases))]
    public Task R9_successful_checkpoint_wait_rechecks_current_owner_and_host_policy_before_handoff(string phase, bool delegated, bool withdraw, bool hostPolicy)
        => RunCheckpointAuthority(phase, delegated, withdraw, hostPolicy, false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task R9_host_result_payload_is_withheld_from_UI_when_authority_changes_during_a_successful_checkpoint(bool hostPolicy)
        => RunCheckpointAuthority("result", false, true, hostPolicy, true);

    private static async Task RunCheckpointAuthority(string phase, bool delegated, bool withdraw, bool hostPolicy, bool inspectPayload)
    {
        var policy = new EvidencePolicy();
        using var fixture = new Fixture(delegated, phase == "callback" ? Script.ReadThenAnswer : phase is "result" or "receipt" ? Script.PauseThenAnswer : Script.Answer,
            services =>
            {
                AddBoundaryStores(services, new RecordingLogger());
                services.Replace(ServiceDescriptor.Scoped<IAgentPolicyEvaluator>(_ => policy));
            });
        if (phase == "result") fixture.Policy = AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto };
        if (phase == "callback") fixture.Probe.BeforeOperation = () => fixture.Source.AddSection = true;
        await fixture.Post("Use the current admitted resources");
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        AgentToolResult? accepted = null;
        string? proposalId = null;
        if (phase == "receipt")
        {
            var first = await fixture.Stream();
            var proposal = Assert.IsType<AgentActionProposal>(Assert.Single(first, item => item.Kind == AgentStreamEventKind.ProposalCreated).Payload);
            proposalId = proposal.Id;
            var proposals = fixture.Services.GetRequiredService<IAgentProposalService>();
            Assert.True((await proposals.ApproveAsync(proposal.Id, fixture.Session!.ActorId, proposal.BaseRevision)).Succeeded);
            Assert.True((await proposals.ExecuteAsync(proposal.Id, fixture.Session.ActorId, proposal.BaseRevision)).Succeeded);
            accepted = await proposals.FindResultAsync(proposal.Id);
        }
        store.HoldAssistantAt = phase is "initial" or "receipt" ? store.AssistantInsertCalls + 1 : 2;
        var running = fixture.Stream();
        await store.SuccessfulWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (withdraw)
        {
            if (hostPolicy) policy.DenyDetail = true;
            else fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        }
        store.ReleaseSuccessfulWrite.TrySetResult();

        var events = await running.WaitAsync(TimeSpan.FromSeconds(30));

        if (withdraw)
        {
            Assert.Equal(phase == "initial" ? 0 : 1, fixture.Probe.Inputs.Count);
            Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Error);
            Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Completed);
            Assert.False(fixture.Probe.AfterCallbackEntered);
            if (phase == "callback") Assert.Equal(0, fixture.Read.Preparations);
        }
        else Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Completed);
        if (inspectPayload)
            Assert.DoesNotContain(events, item => item.Kind is AgentStreamEventKind.ToolCallCompleted or AgentStreamEventKind.ContributedResult);
        Assert.Equal(phase is "result" or "receipt" ? 1 : 0, fixture.Mutation.ExecuteCalls);
        if (phase == "receipt") Assert.Equal(accepted, await fixture.Services.GetRequiredService<IAgentProposalService>().FindResultAsync(proposalId!));
        if (phase == "result")
        {
            var proposal = Assert.Single(fixture.Services.GetRequiredService<InMemoryAgentProposalStore>().Proposals.Values);
            Assert.NotNull((await fixture.Services.GetRequiredService<IAgentProposalService>().FindResultAsync(proposal.Id))?.Receipt);
        }
        Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session!.Id));
    }

    public static IEnumerable<object[]> ProviderBoundaryCases()
    {
        foreach (var delegated in new[] { false, true })
        {
            yield return ["open", delegated, false];
            foreach (var phase in new[] { "advance", "dispose", "dispatch" })
            foreach (var asynchronous in new[] { false, true })
                yield return [phase, delegated, asynchronous];
        }
    }

    [Theory]
    [MemberData(nameof(ProviderBoundaryCases))]
    public async Task R9_provider_and_module_dispatch_causes_survive_secondary_cleanup(string phase, bool delegated, bool asynchronous)
    {
        var failure = new IOException("Private provider boundary cause " + Guid.NewGuid().ToString("N"));
        var cleanup = new IOException("Private provider cleanup cause " + Guid.NewGuid().ToString("N"));
        using var fixture = new Fixture(delegated, phase == "dispatch" ? Script.ReadThenAnswer : Script.Answer, services =>
        {
            AddBoundaryStores(services, new RecordingLogger());
            var original = Assert.IsAssignableFrom<IAgentProvider>(Assert.Single(services, d => d.ServiceType == typeof(IAgentProvider)).ImplementationInstance);
            if (phase == "dispatch")
                services.Replace(ServiceDescriptor.Scoped<IAgentToolInvoker>(_ => new FailingDispatch(failure, asynchronous)));
            else services.Replace(ServiceDescriptor.Singleton<IAgentProvider>(delegated
                ? new FailingHarness(original, phase, failure, asynchronous)
                : new FailingProvider(original, phase, failure, asynchronous)));
        });
        fixture.CaptureDomainFailures = true;
        await fixture.Post("Preserve provider boundary cause through cleanup");
        fixture.Services.GetRequiredService<SessionBoundary>().ThrowOnTerminalStatus = cleanup;

        await fixture.Stream();

        var domain = Assert.Single(fixture.DomainFailures);
        var chain = ExceptionChain(domain).ToArray();
        Assert.Contains(chain, cause => ReferenceEquals(cause, failure));
        Assert.Contains(chain, cause => ReferenceEquals(cause, cleanup));
        Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session!.Id));
        Assert.Equal(0, fixture.Read.Preparations);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
        if (delegated && phase == "dispatch")
        {
            Assert.NotNull(fixture.Probe.CallbackCancellation);
            Assert.DoesNotContain(failure.Message, fixture.Probe.CallbackCancellation!.Message, StringComparison.Ordinal);
        }
    }

    private sealed class FailingDispatch(Exception failure, bool asynchronous) : IAgentToolInvoker
    {
        public Task<AgentToolDispatch> DispatchAsync(AgentToolInvocation invocation, AgentPolicy policy, CancellationToken cancellationToken = default)
            => asynchronous ? Task.FromException<AgentToolDispatch>(failure) : throw failure;
    }

    private class FailingProvider(IAgentProvider inner, string phase, Exception failure, bool asynchronous) : IAgentProvider
    {
        public string ProviderId => inner.ProviderId;
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default) => inner.CreateSessionAsync(session, cancellationToken);
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => inner.ApproveToolAsync(request, cancellationToken);
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => inner.GetDiagnosticsAsync(cancellationToken);
        public IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, CancellationToken cancellationToken = default)
            => Guard(inner.ContinueTurnAsync(context, cancellationToken));
        protected IAsyncEnumerable<AgentStreamEvent> Guard(IAsyncEnumerable<AgentStreamEvent> stream)
            => phase == "open" ? throw failure : new FailingEnumerable(stream, phase, failure, asynchronous);
    }
    private sealed class FailingHarness(IAgentProvider provider, string phase, Exception failure, bool asynchronous)
        : FailingProvider(provider, phase, failure, asynchronous), IAgentHarness
    {
        public AgentHarnessCapabilities Capabilities => ((IAgentHarness)provider).Capabilities;
        public IAsyncEnumerable<AgentStreamEvent> RunTurnAsync(AgentHarnessTurnRequest request, CancellationToken cancellationToken = default)
            => Guard(((IAgentHarness)provider).RunTurnAsync(request, cancellationToken));
    }
    private sealed class FailingEnumerable(IAsyncEnumerable<AgentStreamEvent> inner, string phase, Exception failure, bool asynchronous) : IAsyncEnumerable<AgentStreamEvent>
    {
        public IAsyncEnumerator<AgentStreamEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new Enumerator(inner.GetAsyncEnumerator(cancellationToken), phase, failure, asynchronous);
        private sealed class Enumerator(IAsyncEnumerator<AgentStreamEvent> inner, string phase, Exception failure, bool asynchronous) : IAsyncEnumerator<AgentStreamEvent>
        {
            public AgentStreamEvent Current => inner.Current;
            public ValueTask<bool> MoveNextAsync() => phase == "advance"
                ? asynchronous ? ValueTask.FromException<bool>(failure) : throw failure : inner.MoveNextAsync();
            public async ValueTask DisposeAsync()
            {
                await inner.DisposeAsync();
                if (phase == "dispose")
                {
                    if (asynchronous) await Task.Yield();
                    throw failure;
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R9_benign_payload_refresh_keeps_the_checkpointed_binding_and_allows_the_tool(bool delegated)
    {
        using var fixture = new Fixture(delegated, Script.ReadThenAnswer);
        fixture.Source.RefreshPayload = true;
        await fixture.Post("Read with a current authorized binding whose summary refreshes");

        var events = await fixture.Stream();

        Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Completed);
        Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Error);
        Assert.Equal(1, fixture.Read.Preparations);
        Assert.Equal(1, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Theory]
    [InlineData(false, "scopes")]
    [InlineData(true, "scopes")]
    [InlineData(false, "revision")]
    [InlineData(true, "revision")]
    [InlineData(false, "target")]
    [InlineData(true, "target")]
    public async Task R9_binding_change_during_callback_checkpoint_requires_fresh_intent(bool delegated, string change)
    {
        using var fixture = new Fixture(delegated, Script.ReadThenAnswer, services => AddBoundaryStores(services, new RecordingLogger()));
        fixture.Probe.BeforeOperation = () => fixture.Source.AddSection = true;
        await fixture.Post("Keep this accepted operation bound to its original context");
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        store.HoldAssistantAt = 2;
        var running = fixture.Stream();
        await store.SuccessfulWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        fixture.Source.BindingDrift = change;
        store.ReleaseSuccessfulWrite.TrySetResult();

        var events = await running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(0, fixture.Read.Preparations);
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
        Assert.Contains(events, item => item.Error?.Code == "agent.tool.context_changed");
        Assert.False(fixture.Probe.AfterCallbackEntered);
    }

    private static void AddBoundaryStores(IServiceCollection services, RecordingLogger logger)
    {
        services.AddRouting().AddElsaEndpoints();
        services.AddLogging(builder => builder.AddProvider(new CaptureLoggerProvider(logger)));
        services.AddSingleton<SessionBoundary>(sp => new(new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>())));
        services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => sp.GetRequiredService<SessionBoundary>()));
        services.AddSingleton<PauseBoundary>();
        services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(sp => sp.GetRequiredService<PauseBoundary>()));
    }

    private sealed class CatalogFault { internal Exception? Failure; internal bool Asynchronous; }
    private sealed class BoundaryCatalog(IAgentToolRegistry inner, CatalogFault fault) : IAgentToolRegistry
    {
        public Task<IReadOnlyCollection<IAgentTool>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (fault.Failure is not { } failure) return inner.ListAsync(cancellationToken);
            if (fault.Asynchronous) return Task.FromException<IReadOnlyCollection<IAgentTool>>(failure);
            throw failure;
        }
        public Task<IAgentTool?> FindAsync(string toolName, CancellationToken cancellationToken = default) => inner.FindAsync(toolName, cancellationToken);
    }
    private sealed class RegistrationFault : IAgentTurnRegistry
    {
        private readonly AgentTurnRegistry inner = new();
        internal Exception? Failure;
        public CancellationToken Register(string sessionId, string turnId) => Failure is { } failure ? throw failure : inner.Register(sessionId, turnId);
        public bool Cancel(string sessionId, string turnId) => inner.Cancel(sessionId, turnId);
        public void Unregister(string sessionId, string turnId) => inner.Unregister(sessionId, turnId);
        public bool IsActive(string sessionId) => inner.IsActive(sessionId);
    }
    private sealed class ProviderRegistryFault : IAgentProviderRegistry
    {
        internal IAgentProvider? Provider;
        internal Exception? Failure;
        public IAgentProvider? Active => Failure is { } failure ? throw failure : Provider;
    }
    private sealed class EvidencePolicy : IAgentPolicyEvaluator
    {
        private readonly DefaultAgentPolicyEvaluator inner = new();
        internal bool DenyDetail;
        public ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default) => inner.EvaluateAvailabilityAsync(policy, cancellationToken);
        public ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default) => inner.EvaluateCapabilityAsync(policy, capabilityId, cancellationToken);
        public ValueTask<AgentPolicyDecision> EvaluateContextAsync(AgentPolicy policy, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
            => DenyDetail && attachments.Any(item => item.Target?.ResourceId == "detail-resource")
                ? ValueTask.FromResult(new AgentPolicyDecision(false, [new("fixture.policy_denied", "Current host policy withholds the detail resource.")]))
                : inner.EvaluateContextAsync(policy, attachments, cancellationToken);
    }
}
