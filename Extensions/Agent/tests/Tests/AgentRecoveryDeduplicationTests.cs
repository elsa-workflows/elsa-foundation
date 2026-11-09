using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 T013 (FR-009, stories 2.5 and 2.7): recorded fingerprints and duplicate-detection keys keep their meaning across
/// a restart. Retries get the original disposition, and nothing is prepared, executed or continued twice.
/// </summary>
public sealed class AgentRecoveryDeduplicationTests
{
    [Fact]
    public async Task A_paused_turn_with_a_recorded_receipt_continues_once_from_its_verbatim_fingerprint()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        var proposal = await harness.Host.PauseOnReviewAsync(session);
        Assert.True((await harness.Host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.True((await harness.Host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value!.Executed);
        var paused = (await harness.Host.Pauses.FindAsync(session.Id))!;

        var host = harness.Restart();

        var recovered = (await host.Pauses.FindAsync(session.Id))!;
        Assert.NotSame(paused, recovered);
        Assert.Equal(paused.DisclosureHistoryFingerprint, recovered.DisclosureHistoryFingerprint);
        Assert.Equal(AgentDisclosureHistory.PauseFingerprint(paused), AgentDisclosureHistory.PauseFingerprint(recovered));
        var turns = harness.Provider.Turns;
        Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);
        Assert.Equal(AgentStreamEventKind.Error, (await host.StreamAsync(session))[^1].Kind);
        Assert.Equal(turns + 1, harness.Provider.Turns);
        Assert.Equal(1, harness.Module.Effects);
        // The receipt the continuation recorded is one Tool message with its original invocation identity.
        var receipts = (await host.Sessions.ListMessagesAsync(session.Id)).Where(x => x.Role == AgentRole.Tool).ToArray();
        Assert.Equal(proposal.PreparedInvocation!.InvocationId, Assert.Single(receipts).RequestId);
    }

    [Fact]
    public async Task An_identical_retry_of_an_accepted_prompt_returns_the_original_message_after_a_restart()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        var accepted = await harness.Host.PostAsync(session, "hello", "request-1", "fingerprint-1");

        var host = harness.Restart();

        var retried = await host.PostAsync(session, "hello", "request-1", "fingerprint-1");
        Assert.Equal(accepted.Id, retried.Id);
        Assert.Equal(AgentInvocationIntegrity.Digest(accepted), AgentInvocationIntegrity.Digest(retried));
        await Assert.ThrowsAsync<AgentContributionException>(() => host.PostAsync(session, "changed", "request-1", "fingerprint-2"));
        Assert.Single(await host.Sessions.ListMessagesAsync(session.Id));
    }

    [Fact]
    public async Task Duplicate_call_and_call_input_keys_deduplicate_after_a_restart()
    {
        using var harness = new AgentRestartHarness();
        var session = await harness.Host.CreateAsync();
        var original = (await harness.Host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;

        var host = harness.Restart();

        var repeated = await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, repeated.Outcome);
        Assert.Equal(original.Id, repeated.Proposal!.Id);
        var conflicting = AgentRecoveryRestartTests.Call(session) with { Arguments = new Dictionary<string, object?> { ["change"] = "remove-activity" } };
        Assert.Equal("agent.tool.duplicate_conflict", (await host.Invoker.DispatchAsync(conflicting, session.Policy)).Error?.Code);
        // Neither the identical nor the conflicting retry reached the module again.
        Assert.Equal(1, harness.Module.Preparations);
        // The turn's recorded mutation still refuses another call in the same turn.
        Assert.Equal("agent.tool.turn_mutation_limit",
            (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session, "call-2"), session.Policy)).Error?.Code);
        Assert.Single(host.ProposalStore.Proposals.Values);
    }

    [Fact]
    public async Task An_accepted_acknowledgement_deduplicates_after_a_restart_and_a_conflicting_one_is_refused()
    {
        var client = new ProposalClientTool();
        using var harness = new AgentRestartHarness(configure: services => services.AddSingleton<IAgentTool>(client));
        var session = await harness.Host.CreateAsync();
        var proposal = (await harness.Host.Invoker.DispatchAsync(AgentRecoveryRestartTests.ClientCall(session, client), session.Policy)).Proposal!;
        Assert.True((await harness.Host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        await harness.Host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        var invocationId = proposal.PreparedInvocation!.InvocationId;
        var accepted = await harness.Host.Proposals.AcknowledgeAsync(session.Id, invocationId, session.ActorId, AgentRecoveryRestartTests.Acknowledgement(proposal));
        Assert.True(accepted.Value!.Executed);

        var host = harness.Restart();

        var retried = await host.Proposals.AcknowledgeAsync(session.Id, invocationId, session.ActorId, AgentRecoveryRestartTests.Acknowledgement(proposal));
        Assert.True(retried.Value!.Executed);
        Assert.Equal(accepted.Value.ToolResult!.Receipt!.InvocationId, retried.Value.ToolResult!.Receipt!.InvocationId);
        var conflicting = AgentRecoveryRestartTests.Acknowledgement(proposal) with { Summary = "A different observation." };
        Assert.Equal("agent.invocation.acknowledgement_conflict",
            (await host.Proposals.AcknowledgeAsync(session.Id, invocationId, session.ActorId, conflicting)).Error?.Code);
        Assert.Equal(1, client.CompleteCalls);
    }
}
