using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 self-review round 1 (S4): a recovery settlement's audit records are recorded with the settlement and cleared
/// only once emitted, so a process that stops (or whose sink fails) in between never loses them. Compiled into the
/// durable EF suite too.
/// </summary>
public sealed class AgentRecoveryAuditTests
{
    [Fact]
    public async Task A_settlement_whose_audit_was_not_emitted_emits_it_at_the_next_hydration_exactly_once()
    {
        var sink = new FailingAuditSink();
        using var harness = new AgentRestartHarness(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink>(sp => sink.Bind(sp.GetRequiredService<InMemoryAgentAuditStore>()))));
        var session = await harness.Host.CreateAsync();
        var proposal = (await harness.Host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        var b = harness.Restart();
        sink.FailNext();

        // The settlement is recorded, but its audit record never reached the sink.
        await Assert.ThrowsAsync<InvalidOperationException>(() => b.Sessions.FindAsync(session.Id));
        var recorded = (await harness.Store.LoadAsync(session.Id))!;
        Assert.Equal(AgentActionProposalStatus.Expired, recorded.Proposals.Single().Proposal.Status);
        Assert.Empty(await b.AuditAsync(session));

        var c = harness.Restart();
        Assert.NotNull(await c.Sessions.FindAsync(session.Id));
        var settled = Assert.Single(await c.AuditAsync(session), x => x.Metadata.GetValueOrDefault(AgentProposalRecovery.SettledBy) == AgentProposalRecovery.Recovery);
        Assert.Equal((proposal.Id, nameof(AgentActionProposalStatus.Expired)), (settled.Metadata["proposalId"], settled.Metadata["status"]));
        Assert.Empty((await harness.Store.LoadAsync(session.Id))!.PendingAudits);

        // Once emitted, nothing is owed: a later hydration emits nothing again.
        var d = harness.Restart();
        Assert.NotNull(await d.Sessions.FindAsync(session.Id));
        Assert.Empty(await d.AuditAsync(session));
    }

    [Fact]
    public async Task The_same_process_retries_owed_audit_records_on_its_next_access()
    {
        var sink = new FailingAuditSink();
        using var harness = new AgentRestartHarness(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink>(sp => sink.Bind(sp.GetRequiredService<InMemoryAgentAuditStore>()))));
        var session = await harness.Host.CreateAsync();
        var proposal = (await harness.Host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        var b = harness.Restart();
        sink.FailNext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => b.Sessions.FindAsync(session.Id));

        // Nothing was published for the session, so the next access in the same process emits what is owed.
        Assert.NotNull(await b.Sessions.FindAsync(session.Id));

        var settled = Assert.Single(await b.AuditAsync(session), x => x.Metadata.GetValueOrDefault(AgentProposalRecovery.SettledBy) == AgentProposalRecovery.Recovery);
        Assert.Equal(proposal.Id, settled.Metadata["proposalId"]);
        Assert.Empty((await harness.Store.LoadAsync(session.Id))!.PendingAudits);
        Assert.Equal(AgentActionProposalStatus.Expired, (await b.Proposals.FindAsync(proposal.Id))!.Status);
    }

    /// <summary>Forwards to the host's audit store; the next emission can be made to fail before it records anything.</summary>
    private sealed class FailingAuditSink : IAgentAuditSink
    {
        private IAgentAuditSink _inner = null!;
        private int _fail;

        public FailingAuditSink Bind(IAgentAuditSink inner)
        {
            _inner = inner;
            return this;
        }

        public void FailNext() => Volatile.Write(ref _fail, 1);

        public Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default)
            => Interlocked.Exchange(ref _fail, 0) == 1
                ? throw new InvalidOperationException("The audit sink is unavailable.")
                : _inner.EmitAsync(auditEvent, cancellationToken);
    }
}
