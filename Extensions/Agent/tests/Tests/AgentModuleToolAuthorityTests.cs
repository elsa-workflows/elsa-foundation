using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentModuleToolAuthorityTests
{
    [Fact]
    public async Task Module_authority_can_withhold_a_tool_from_provider_advertisement_despite_host_permission()
    {
        using var fixture = new Fixture();
        fixture.Tool.Allowed = false;
        var session = await fixture.Session();
        await fixture.Sessions.AddMessageAsync(session.Id, new(AgentRole.User, "Explain the current resource.", AgentMessageStatus.Pending, null, [], []));

        await foreach (var _ in fixture.Services.GetRequiredService<IAgentStreamingService>().StreamAsync(session.Id)) { }

        Assert.Empty(Assert.Single(fixture.Provider.Inputs).AvailableTools);
        Assert.True(fixture.Tool.AuthorityCalls > 0);
        Assert.Equal(0, fixture.Tool.Inner.PrepareCalls);
        Assert.Equal(0, fixture.Tool.Inner.ExecuteCalls);
    }

    [Fact]
    public async Task Module_authority_refuses_direct_full_auto_execution_before_any_effect()
    {
        using var fixture = new Fixture();
        fixture.Tool.Allowed = false;
        var session = await fixture.Session();

        var dispatch = await fixture.Invoke(session);

        Assert.Equal(AgentToolDispatchOutcome.Denied, dispatch.Outcome);
        Assert.Equal("agent.tool.forbidden", dispatch.Error?.Code);
        Assert.Equal(0, fixture.Tool.Inner.ExecuteCalls);
        Assert.Equal("initial", fixture.Tool.Inner.Value);
    }

    [Fact]
    public async Task Module_authority_is_rechecked_after_genuine_approval_before_dispatch()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session(review: true);
        var dispatch = await fixture.Invoke(session);
        var proposal = Assert.IsType<AgentActionProposal>(dispatch.Proposal);
        var approved = await fixture.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.True(approved.Succeeded, approved.Error?.Message);
        fixture.Tool.Allowed = false;

        var result = await fixture.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.False(result.Succeeded);
        Assert.Equal("agent.proposal.forbidden", result.Error?.Code);
        Assert.Equal(0, fixture.Tool.Inner.ExecuteCalls);
        Assert.Equal("initial", fixture.Tool.Inner.Value);
    }

    [Fact]
    public async Task A_duplicate_delivery_cannot_return_a_retained_outcome_after_module_authority_is_lost()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session();
        var first = await fixture.Invoke(session);
        Assert.Equal(AgentToolDispatchOutcome.Executed, first.Outcome);
        var receipt = Assert.IsType<AgentEffectReceipt>(first.Result?.Receipt);
        fixture.Tool.Allowed = false;

        var duplicate = await fixture.Invoke(session);

        Assert.Equal(AgentToolDispatchOutcome.Denied, duplicate.Outcome);
        Assert.Equal("agent.tool.forbidden", duplicate.Error?.Code);
        Assert.Null(duplicate.Result);
        Assert.Equal(1, fixture.Tool.Inner.ExecuteCalls);
        Assert.Equal("new", fixture.Tool.Inner.Value);
        var original = Assert.Single(fixture.Services.GetRequiredService<InMemoryAgentProposalStore>().Proposals.Values);
        Assert.Equal(receipt, (await fixture.Proposals.FindResultAsync(original.Id))?.Receipt);
    }

    [Fact]
    public async Task A_module_permitting_the_target_keeps_the_existing_once_only_effect_and_receipt()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session();

        var dispatch = await fixture.Invoke(session);

        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        Assert.True(fixture.Tool.AuthorityCalls > 0);
        Assert.Equal(1, fixture.Tool.Inner.ExecuteCalls);
        Assert.Equal("new", fixture.Tool.Inner.Value);
        Assert.Equal(fixture.Tool.Inner.Revision, dispatch.Result?.Receipt?.After.Revision);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        internal OwnerTool Tool { get; } = new();
        internal ProviderProbe Provider { get; } = new();
        internal IServiceProvider Services => scope.ServiceProvider;
        internal IAgentSessionService Sessions => Services.GetRequiredService<IAgentSessionService>();
        internal IAgentProposalService Proposals => Services.GetRequiredService<IAgentProposalService>();
        internal Fixture()
        {
            root = AgentTestServices.Create(services =>
            {
                services.AddSingleton<IAgentTool>(Tool);
                services.AddSingleton<IAgentProvider>(Provider);
            });
            scope = root.CreateScope();
        }
        internal Task<AgentSession> Session(bool review = false) => Sessions.CreateAsync(new("tenant-1", "actor-1", "conversation",
            Provider.ProviderId, "build", null, AgentPolicy.Default with
            { AutonomyMode = review ? AgentAutonomyMode.AutoReadOnly : AgentAutonomyMode.FullAuto }, new Dictionary<string, string>()));
        internal Task<AgentToolDispatch> Invoke(AgentSession session) => Services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(
            new("original-call", Tool.Descriptor.Name, session.Id, session.ActorId, new Dictionary<string, object?> { ["value"] = "new" }, [])
            { TenantId = session.TenantId, TurnId = "original-turn" }, session.Policy);
        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class OwnerTool : IAgentTool, IAgentToolAuthorization
    {
        internal ProposalMutationTool Inner { get; } = new();
        internal bool Allowed { get; set; } = true;
        internal int AuthorityCalls { get; private set; }
        public AgentToolDescriptor Descriptor => Inner.Descriptor;
        public ValueTask<AgentPolicyDecision> AuthorizeAsync(string actorId, string tenantId, AgentResourceTarget? target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthorityCalls++;
            return ValueTask.FromResult(Allowed ? new AgentPolicyDecision(true, [])
                : new(false, [new("module.resource.denied", "The owning module withholds access to this resource.")]));
        }
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Inner.PrepareAsync(invocation, cancellationToken);
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Inner.ExecuteAsync(invocation, cancellationToken);
    }

    private sealed class ProviderProbe : IAgentProvider
    {
        public string ProviderId => "module-authority-probe";
        internal List<AgentTurnContext> Inputs { get; } = [];
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Inputs.Add(context);
            await Task.Yield();
            yield return new("complete", AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(request.Approved, string.Empty));
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "available", AgentProviderKind.ProviderSdkBinding,
                [AgentProviderOperation.Chat], AgentProviderRiskProfile.ReviewRequired, new Dictionary<string, string>()));
    }
}
