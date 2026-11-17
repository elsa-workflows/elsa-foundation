using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

/// <summary>
/// The engine's owner-unavailable outcome (spec 1193): only the installed tool's own decision, with an owner-namespaced code,
/// reaches 503; the owner's own message never does; host violations and reserved engine codes stay ordinary denials.
/// </summary>
public sealed class WeaverOwnerUnavailableTests
{
    private const string OwnerCode = "module.authority.unavailable";
    private const string FixedMessage = "The owning module could not establish current authority for this operation.";
    private const string Cause = "Private owner cause 7c41 at db-primary:5432";

    [Fact]
    public async Task An_owner_that_cannot_decide_is_reported_unavailable_with_its_code_and_only_the_fixed_message()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session();
        fixture.Tool.Decision = Unavailable(OwnerCode);

        var dispatch = await fixture.Invoke(session);

        Assert.Equal(WeaverToolDispatchOutcome.Denied, dispatch.Outcome);
        Assert.Equal(new WeaverError(OwnerCode, FixedMessage, 503), dispatch.Error);
        Assert.DoesNotContain(Cause, dispatch.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Tool.Inner.ExecuteCalls);
    }

    [Fact]
    public async Task A_duplicate_delivery_whose_owner_can_no_longer_decide_is_unavailable_without_the_retained_outcome()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session();
        Assert.Equal(WeaverToolDispatchOutcome.Executed, (await fixture.Invoke(session)).Outcome);
        fixture.Tool.Decision = Unavailable(OwnerCode);

        var duplicate = await fixture.Invoke(session);

        Assert.Equal(WeaverToolDispatchOutcome.Denied, duplicate.Outcome);
        Assert.Null(duplicate.Result);
        Assert.Equal(new WeaverError(OwnerCode, FixedMessage, 503), duplicate.Error);
        Assert.Equal(1, fixture.Tool.Inner.ExecuteCalls);
    }

    [Fact]
    public async Task An_approved_operation_whose_owner_can_no_longer_decide_is_not_executed()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session(review: true);
        var proposal = Assert.IsType<WeaverActionProposal>((await fixture.Invoke(session)).Proposal);
        Assert.True((await fixture.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        fixture.Tool.Decision = Unavailable(OwnerCode);

        var execution = await fixture.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.False(execution.Succeeded);
        Assert.Equal(new WeaverError(OwnerCode, FixedMessage, 503), execution.Error);
        Assert.Equal(0, fixture.Tool.Inner.ExecuteCalls);
    }

    [Fact]
    public async Task A_host_denial_marked_unavailable_stays_forbidden_and_never_reaches_the_owner()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session();
        fixture.Host.Decision = Unavailable(OwnerCode);

        var dispatch = await fixture.Invoke(session);

        Assert.Equal("weaver.tool.forbidden", dispatch.Error?.Code);
        Assert.Equal(403, dispatch.Error?.StatusCode);
        Assert.Equal(0, fixture.Tool.AuthorityCalls);
    }

    [Fact]
    public async Task A_host_advisory_marked_unavailable_cannot_turn_an_owner_s_plain_denial_into_unavailable()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session();
        fixture.Host.Decision = new(true, [new(OwnerCode, "Advisory host note.") { OwnerUnavailable = true }]);
        fixture.Tool.Decision = new(false, [new("module.resource.denied", "The owning module withholds access to this resource.")]);

        var dispatch = await fixture.Invoke(session);

        Assert.Equal("weaver.tool.forbidden", dispatch.Error?.Code);
        Assert.Equal(403, dispatch.Error?.StatusCode);
        Assert.Equal(1, fixture.Tool.AuthorityCalls);
    }

    [Theory]
    [InlineData("weaver.proposal.expired")]
    [InlineData("Weaver.Tool.Forbidden")]
    [InlineData("weaver.authority.unavailable")]
    [InlineData("")]
    [InlineData("module authority unavailable")]
    [InlineData("unavailable")]
    [InlineData("module.unavailable\n")]
    public async Task An_owner_cannot_spoof_a_reserved_or_malformed_code_through_the_unavailable_outcome(string code)
    {
        using var fixture = new Fixture();
        var session = await fixture.Session();
        fixture.Tool.Decision = Unavailable(code);

        var dispatch = await fixture.Invoke(session);

        Assert.Equal("weaver.tool.forbidden", dispatch.Error?.Code);
        Assert.Equal(403, dispatch.Error?.StatusCode);
    }

    [Fact]
    public async Task An_overlong_owner_code_stays_an_ordinary_denial()
    {
        using var fixture = new Fixture();
        var session = await fixture.Session();
        fixture.Tool.Decision = Unavailable("module." + new string('a', 128));

        Assert.Equal("weaver.tool.forbidden", (await fixture.Invoke(session)).Error?.Code);
    }

    private static WeaverPolicyDecision Unavailable(string code) => new(false, [new(code, "Owner failed: " + Cause) { OwnerUnavailable = true }]);

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        internal OwnerTool Tool { get; } = new();
        internal HostAuthority Host { get; } = new();
        internal IServiceProvider Services => scope.ServiceProvider;
        internal IWeaverSessionService Sessions => Services.GetRequiredService<IWeaverSessionService>();
        internal IWeaverProposalService Proposals => Services.GetRequiredService<IWeaverProposalService>();

        internal Fixture()
        {
            root = WeaverTestServices.Create(services =>
            {
                services.AddSingleton<IWeaverTool>(Tool);
                services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer>(_ => Host));
            });
            scope = root.CreateScope();
        }

        internal Task<WeaverSession> Session(bool review = false) => Sessions.CreateAsync(new("tenant-1", "actor-1", "conversation",
            "test-provider", "build", null, WeaverPolicy.Default with
            { AutonomyMode = review ? WeaverAutonomyMode.AutoReadOnly : WeaverAutonomyMode.FullAuto }, new Dictionary<string, string>()));

        internal Task<WeaverToolDispatch> Invoke(WeaverSession session) => Services.GetRequiredService<IWeaverToolInvoker>().DispatchAsync(
            new("original-call", Tool.Descriptor.Name, session.Id, session.ActorId, new Dictionary<string, object?> { ["value"] = "new" }, [])
            { TenantId = session.TenantId, TurnId = "original-turn" }, session.Policy);

        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class HostAuthority : IWeaverToolAuthorizer
    {
        internal WeaverPolicyDecision Decision { get; set; } = new(true, []);
        public Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId,
            WeaverResourceTarget? target, CancellationToken cancellationToken = default) => Task.FromResult(Decision);
    }

    private sealed class OwnerTool : IWeaverTool, IWeaverToolAuthorization
    {
        internal ProposalMutationTool Inner { get; } = new();
        internal WeaverPolicyDecision Decision { get; set; } = new(true, []);
        internal int AuthorityCalls { get; private set; }
        public WeaverToolDescriptor Descriptor => Inner.Descriptor;

        public ValueTask<WeaverPolicyDecision> AuthorizeAsync(string actorId, string tenantId, WeaverResourceTarget? target,
            CancellationToken cancellationToken = default)
        {
            AuthorityCalls++;
            return ValueTask.FromResult(Decision);
        }

        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
            => Inner.PrepareAsync(invocation, cancellationToken);

        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Inner.ExecuteAsync(invocation, cancellationToken);
    }
}
