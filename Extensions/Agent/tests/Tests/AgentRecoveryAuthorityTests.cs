using Elsa.Agent.Api.Extensions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 T017 (story 4, FR-012, SC-004): only a present, authenticated owner's current authority can cause disclosure or
/// execution during recovery. Recovery without a user records only zero-effect lifecycle outcomes attributed to
/// recovery, and a permission revoked since approval refuses execution with zero effect. Every host here uses the HTTP
/// tool authorizer, which reads the current request's principal.
/// </summary>
public sealed class AgentRecoveryAuthorityTests
{
    private const string Permission = "test/resource:apply";
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PastPreparedDeadline = TimeSpan.FromMinutes(6);

    [Fact]
    public async Task Recovery_with_no_authenticated_user_records_only_recovery_lifecycle_outcomes()
    {
        var permissions = new RevocablePermissions();
        var disclosures = new CountingContextSource();
        using var harness = Harness(permissions, disclosures);
        var old = harness.Host;
        var pending = await old.CreateAsync();
        // The work before the restart is the owner's own: their request is current while it is admitted.
        var accessor = old.Services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = old.Services, User = AgentRecoveryApi.Owner(pending) };
        var review = await old.PauseOnReviewAsync(pending);
        var approved = await old.CreateAsync();
        var approval = await ApproveAsync(old, approved);
        var dispatched = await old.CreateAsync();
        var execution = await ApproveAsync(old, dispatched, "call-2");
        var held = harness.Module.HoldNextDispatch();
        var interrupted = Task.Run(() => old.Proposals.ExecuteAsync(execution.Id, dispatched.ActorId, execution.BaseRevision));
        await held.Started.Task.WaitAsync(HandoffTimeout);
        var running = await old.CreateAsync();
        var prompt = await old.PostAsync(running, "hello");
        var turn = harness.Provider.HoldNext();
        var streaming = Task.Run(() => old.StreamAsync(running));
        await turn.Entered.Task.WaitAsync(HandoffTimeout);
        // The owner's request ends with the process; nothing after the restart runs under it.
        accessor.HttpContext = null;
        harness.Clock.Advance(PastPreparedDeadline);
        var (dispatches, turns) = (harness.Module.Dispatches, harness.Provider.Turns);

        var host = harness.Restart();
        disclosures.Reset();
        // Housekeeping with no request: nothing carries an authenticated principal.
        Assert.Null(host.Services.GetRequiredService<IHttpContextAccessor>().HttpContext);
        foreach (var session in new[] { pending, approved, dispatched, running })
            Assert.True(await host.Owner.HydrateSessionAsync(session.Id));

        var recorded = new List<AgentAuditEvent>();
        foreach (var session in new[] { pending, approved, dispatched, running })
            recorded.AddRange(await host.AuditAsync(session));
        // Expired twice with zero effect and one Uncertain; the interrupted turn records no audit event of its own.
        Assert.Equal(3, recorded.Count);
        Assert.All(recorded, item =>
        {
            Assert.Equal(AgentAuditEventKind.ProposalFailed, item.Kind);
            Assert.Null(item.ActorId);
            Assert.Equal(AgentProposalRecovery.Recovery, item.Metadata[AgentProposalRecovery.SettledBy]);
        });
        Assert.Equal([nameof(AgentActionProposalStatus.Expired), nameof(AgentActionProposalStatus.Expired), nameof(AgentActionProposalStatus.Uncertain)],
            recorded.Select(x => x.Metadata["status"]).Order().ToArray());
        Assert.Equal(0, disclosures.Calls);
        Assert.Equal((dispatches, turns, 0), (harness.Module.Dispatches, harness.Provider.Turns, harness.Module.Effects));
        Assert.Equal(AgentActionProposalStatus.Expired, (await host.Proposals.FindAsync(review.Id))!.Status);
        Assert.Equal(AgentActionProposalStatus.Expired, (await host.Proposals.FindAsync(approval.Id))!.Status);
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await host.Proposals.FindAsync(execution.Id))!.Status);
        Assert.Equal(AgentMessageStatusReasons.Interrupted, (await host.Sessions.FindMessageAsync(running.Id, prompt.Id))!.StatusReason);

        held.Release.TrySetResult();
        turn.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => interrupted.WaitAsync(HandoffTimeout));
        await Assert.ThrowsAnyAsync<Exception>(() => streaming.WaitAsync(HandoffTimeout));
        // The crashed host's own dispatch may still apply its effect (that is what Uncertain records); recovery added none.
        Assert.Equal(dispatches, harness.Module.Dispatches);
    }

    [Fact]
    public async Task Without_an_authenticated_user_an_approved_operation_fails_closed_after_a_restart()
    {
        var permissions = new RevocablePermissions();
        using var harness = Harness(permissions);
        var session = await harness.Host.CreateAsync();
        var proposal = await ApproveAsync(harness.Host, session);

        var host = harness.Restart();
        var refused = await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.NotNull(refused.Error);
        Assert.Equal(AgentActionProposalStatus.Failed, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Equal(0, harness.Module.Dispatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_returning_owner_executes_only_under_current_permissions(bool revoked)
    {
        var permissions = new RevocablePermissions();
        using var harness = Harness(permissions);
        var session = await harness.Host.CreateAsync();
        var proposal = await ApproveAsync(harness.Host, session);

        var host = harness.Restart();
        if (revoked) permissions.Revoke(Permission);
        var (status, body) = await AgentRecoveryApi.SendAsync(host, "ExecuteProposal", AgentRecoveryApi.Owner(session),
            new Dictionary<string, string> { ["proposalId"] = proposal.Id }, new { revision = proposal.BaseRevision });

        if (!revoked)
        {
            Assert.Equal(200, status);
            Assert.Equal((1, 1), (harness.Module.Dispatches, harness.Module.Effects));
            return;
        }
        Assert.True(status >= 400, body);
        Assert.Equal(AgentActionProposalStatus.Failed, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        // The refusal is retained: restoring the permission does not reopen the refused operation.
        permissions.Grant(Permission);
        var (again, _) = await AgentRecoveryApi.SendAsync(host, "ExecuteProposal", AgentRecoveryApi.Owner(session),
            new Dictionary<string, string> { ["proposalId"] = proposal.Id }, new { revision = proposal.BaseRevision });
        Assert.True(again >= 400);
        Assert.Equal(0, harness.Module.Dispatches);
    }

    private static AgentRestartHarness Harness(RevocablePermissions permissions, CountingContextSource? disclosures = null)
    {
        var harness = new AgentRestartHarness(configure: services =>
        {
            services.AddRouting().AddElsaEndpoints();
            services.AddFoundationAgentApi();
            services.AddSingleton<IPermissionAuthorizationService>(permissions);
            if (disclosures is not null) services.AddSingleton<IAgentContextProvider>(disclosures);
        });
        harness.Module.RequiredPermissions = [Permission];
        return harness;
    }

    /// <summary>Prepares and approves one reviewed operation through the owner's own request.</summary>
    private static async Task<AgentActionProposal> ApproveAsync(RecoveryHost host, AgentSession session, string callId = "call-1")
    {
        // The accessor's request flows down only: an owner request already current is kept, and only one set here ends here.
        var accessor = host.Services.GetRequiredService<IHttpContextAccessor>();
        var owned = accessor.HttpContext is null;
        if (owned) accessor.HttpContext = new DefaultHttpContext { RequestServices = host.Services, User = AgentRecoveryApi.Owner(session) };
        try
        {
            var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session, callId, "turn-" + callId), session.Policy)).Proposal!;
            Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
            return proposal;
        }
        finally { if (owned) accessor.HttpContext = null; }
    }

    /// <summary>The installed permission authority; every permission is granted until it is revoked.</summary>
    private sealed class RevocablePermissions : IPermissionAuthorizationService
    {
        private readonly HashSet<string> _revoked = new(StringComparer.Ordinal);

        public void Revoke(string permission) { lock (_revoked) _revoked.Add(permission); }
        public void Grant(string permission) { lock (_revoked) _revoked.Remove(permission); }

        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            lock (_revoked)
                return ValueTask.FromResult(context.Principal.Identity?.IsAuthenticated == true && !_revoked.Contains(context.Permission)
                    ? PermissionEvaluationResult.Success : PermissionEvaluationResult.Denied("revoked"));
        }
    }

    /// <summary>An installed context source that only counts how often it is asked to collect or admit anything.</summary>
    private sealed class CountingContextSource : IAgentContextProvider
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public void Reset() => Volatile.Write(ref _calls, 0);
        public AgentContextProviderDescriptor Descriptor { get; } = new("recovery.counting", "recovery.module", "recovery.counting", [], ["1"], []);

        public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult(new AgentContextContribution([], []));
        }

        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult(new AgentContextContribution(request.Context, []));
        }
    }
}
