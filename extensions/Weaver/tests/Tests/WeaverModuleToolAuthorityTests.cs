using System.Runtime.CompilerServices;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

public sealed class WeaverModuleToolAuthorityTests
{
    [Fact]
    public async Task Module_authority_can_withhold_a_tool_from_provider_advertisement_despite_host_permission()
    {
        using var fixture = new Fixture();
        fixture.Tool.Allowed = false;
        var session = await fixture.Session();
        await fixture.Sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "Explain the current resource.", WeaverMessageStatus.Pending, null, [], []));

        await foreach (var _ in fixture.Services.GetRequiredService<IWeaverStreamingService>().StreamAsync(session.Id)) { }

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

        Assert.Equal(WeaverToolDispatchOutcome.Denied, dispatch.Outcome);
        Assert.Equal("weaver.tool.forbidden", dispatch.Error?.Code);
        Assert.Equal(0, fixture.Tool.Inner.ExecuteCalls);
        Assert.Equal("initial", fixture.Tool.Inner.Value);
    }

    [Fact]
    public async Task Module_authority_is_rechecked_after_genuine_approval_before_dispatch()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session(review: true);
        var dispatch = await fixture.Invoke(session);
        var proposal = Assert.IsType<WeaverActionProposal>(dispatch.Proposal);
        var approved = await fixture.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.True(approved.Succeeded, approved.Error?.Message);
        fixture.Tool.Allowed = false;

        var result = await fixture.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.False(result.Succeeded);
        Assert.Equal("weaver.proposal.forbidden", result.Error?.Code);
        Assert.Equal(0, fixture.Tool.Inner.ExecuteCalls);
        Assert.Equal("initial", fixture.Tool.Inner.Value);
    }

    [Fact]
    public async Task A_duplicate_delivery_cannot_return_a_retained_outcome_after_module_authority_is_lost()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session();
        var first = await fixture.Invoke(session);
        Assert.Equal(WeaverToolDispatchOutcome.Executed, first.Outcome);
        var receipt = Assert.IsType<WeaverEffectReceipt>(first.Result?.Receipt);
        fixture.Tool.Allowed = false;

        var duplicate = await fixture.Invoke(session);

        Assert.Equal(WeaverToolDispatchOutcome.Denied, duplicate.Outcome);
        Assert.Equal("weaver.tool.forbidden", duplicate.Error?.Code);
        Assert.Null(duplicate.Result);
        Assert.Equal(1, fixture.Tool.Inner.ExecuteCalls);
        Assert.Equal("new", fixture.Tool.Inner.Value);
        var original = Assert.Single(fixture.Services.GetRequiredService<InMemoryWeaverProposalStore>().Proposals.Values);
        Assert.Equal(receipt, (await fixture.Proposals.FindResultAsync(original.Id))?.Receipt);
    }

    [Fact]
    public async Task A_module_permitting_the_target_keeps_the_existing_once_only_effect_and_receipt()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session();

        var dispatch = await fixture.Invoke(session);

        Assert.Equal(WeaverToolDispatchOutcome.Executed, dispatch.Outcome);
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
        internal IWeaverSessionService Sessions => Services.GetRequiredService<IWeaverSessionService>();
        internal IWeaverProposalService Proposals => Services.GetRequiredService<IWeaverProposalService>();
        internal Fixture()
        {
            root = WeaverTestServices.Create(services =>
            {
                services.AddSingleton<IWeaverTool>(Tool);
                services.AddSingleton<IWeaverProvider>(Provider);
            });
            scope = root.CreateScope();
        }
        internal Task<WeaverSession> Session(bool review = false) => Sessions.CreateAsync(new("tenant-1", "actor-1", "conversation",
            Provider.ProviderId, "build", null, WeaverPolicy.Default with
            { AutonomyMode = review ? WeaverAutonomyMode.AutoReadOnly : WeaverAutonomyMode.FullAuto }, new Dictionary<string, string>()));
        internal Task<WeaverToolDispatch> Invoke(WeaverSession session) => Services.GetRequiredService<IWeaverToolInvoker>().DispatchAsync(
            new("original-call", Tool.Descriptor.Name, session.Id, session.ActorId, new Dictionary<string, object?> { ["value"] = "new" }, [])
            { TenantId = session.TenantId, TurnId = "original-turn" }, session.Policy);
        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class OwnerTool : IWeaverTool, IWeaverToolAuthorization
    {
        internal ProposalMutationTool Inner { get; } = new();
        internal bool Allowed { get; set; } = true;
        internal int AuthorityCalls { get; private set; }
        public WeaverToolDescriptor Descriptor => Inner.Descriptor;
        public ValueTask<WeaverPolicyDecision> AuthorizeAsync(string actorId, string tenantId, WeaverResourceTarget? target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthorityCalls++;
            return ValueTask.FromResult(Allowed ? new WeaverPolicyDecision(true, [])
                : new(false, [new("module.resource.denied", "The owning module withholds access to this resource.")]));
        }
        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
            => Inner.PrepareAsync(invocation, cancellationToken);
        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Inner.ExecuteAsync(invocation, cancellationToken);
    }

    private sealed class ProviderProbe : IWeaverProvider
    {
        public string ProviderId => "module-authority-probe";
        internal List<WeaverTurnContext> Inputs { get; } = [];
        public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new WeaverProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));
        public async IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Inputs.Add(context);
            await Task.Yield();
            yield return new("complete", WeaverStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }
        public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new WeaverToolApprovalResult(request.Approved, string.Empty));
        public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WeaverProviderDiagnostics(ProviderId, true, "available", WeaverProviderKind.ProviderSdkBinding,
                [WeaverProviderOperation.Chat], WeaverProviderRiskProfile.ReviewRequired, new Dictionary<string, string>()));
    }
}
