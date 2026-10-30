using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Agent.Services;
using Elsa.Events;
using Elsa.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Tests;

public sealed class AgentProposalTests
{
    [Fact]
    public async Task Approval_binds_the_exact_target_revision_before_any_effect()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();
        var missing = await fixture.Proposals.ApproveAsync(proposal.Id, ProposalFixture.Actor);
        var stale = await fixture.Proposals.ApproveAsync(proposal.Id, ProposalFixture.Actor, "different");
        var execute = await fixture.Proposals.ExecuteAsync(proposal.Id, ProposalFixture.Actor, proposal.BaseRevision);
        Assert.Equal("agent.proposal.revision_conflict", missing.Error?.Code);
        Assert.Equal("agent.proposal.revision_conflict", stale.Error?.Code);
        Assert.Equal("agent.proposal.approval_required", execute.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
    }

    [Fact]
    public async Task Approved_server_change_returns_actual_owning_resource_readback()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var result = await fixture.ExecuteAsync(proposal);
        Assert.True(result.Value?.Executed);
        Assert.Equal("new", fixture.Mutation!.Value);
        Assert.Equal(fixture.Mutation.Revision, result.Value?.ToolResult?.Receipt?.After.Revision);
        Assert.Equal("new", result.Value!.ToolResult!.Payload!.Data.GetProperty("value").GetString());
        Assert.Equal(AgentActionProposalStatus.Executed, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
        Assert.Same(result.Value.ToolResult, await fixture.Proposals.FindResultAsync(proposal.Id));
        var audit = await fixture.Audit.ListAsync(fixture.Session.Id);
        Assert.Contains(audit, x => x.Kind == AgentAuditEventKind.ProposalCreated);
        Assert.Contains(audit, x => x.Kind == AgentAuditEventKind.ProposalApproved);
        Assert.Contains(audit, x => x.Kind == AgentAuditEventKind.ProposalExecuted);
    }

    [Fact]
    public async Task Concurrent_duplicate_execution_coalesces_to_one_effect_and_receipt()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fixture.ExecuteAsync(proposal)));
        Assert.All(results, result => Assert.True(result.Value?.Executed));
        Assert.Equal(1, fixture.Mutation!.ExecuteCalls);
        Assert.All(results, result => Assert.Equal(proposal.PreparedInvocation!.InvocationId, result.Value!.ToolResult!.Receipt!.InvocationId));
    }

    [Fact]
    public async Task In_flight_server_execution_is_not_reported_as_executed()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new ProposalMutationTool { Started = started, Release = release };
        using var fixture = await ProposalFixture.CreateAsync(tool);
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var executing = fixture.ExecuteAsync(proposal);
        await started.Task;
        Assert.Equal(AgentActionProposalStatus.Executing, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
        Assert.Null(await fixture.Proposals.FindResultAsync(proposal.Id));
        release.SetResult();
        Assert.True((await executing).Value?.Executed);
    }

    [Fact]
    public async Task Changed_resource_revision_is_refused_without_an_effect()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        fixture.Mutation!.ChangeByUser("user edit");
        var result = await fixture.ExecuteAsync(proposal);
        Assert.Equal("test.stale", result.Error?.Code);
        Assert.Equal("user edit", fixture.Mutation.Value);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Fact]
    public async Task Revoked_authority_is_rechecked_before_the_server_effect()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        fixture.Authority.Allowed = false;
        var result = await fixture.ExecuteAsync(proposal);
        Assert.Equal("agent.proposal.forbidden", result.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
    }

    [Fact]
    public async Task A_stronger_current_tool_risk_requires_new_review_before_effects()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        fixture.Mutation!.CurrentRisk = AgentRisk.Destructive;
        var result = await fixture.ExecuteAsync(proposal);
        Assert.Equal("agent.proposal.contract_changed", result.Error?.Code);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Fact]
    public async Task A_successful_noop_without_a_receipt_never_becomes_executed()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool { NoopWithoutReceipt = true });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var result = await fixture.ExecuteAsync(proposal);
        Assert.False(result.Value!.Executed);
        Assert.Equal("uncertain", result.Value.Status);
        Assert.Equal("initial", fixture.Mutation!.Value);
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_or_cancellation_after_a_real_effect_is_uncertain_and_cannot_replay(bool cancelled)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool { FailAfterEffect = !cancelled, CancelAfterEffect = cancelled });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var result = await fixture.ExecuteAsync(proposal);
        Assert.Equal("uncertain", result.Value?.Status);
        Assert.False(result.Value!.Executed);
        Assert.Equal("new", fixture.Mutation!.Value);
        Assert.DoesNotContain("secret-test-value", result.Value.Summary);
        await fixture.ExecuteAsync(proposal);
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
    }

    [Fact]
    public async Task A_null_client_acknowledgement_cannot_overwrite_a_real_server_effects_uncertainty()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool { FailAfterEffect = true });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var original = await fixture.ExecuteAsync(proposal);
        Assert.Equal("new", fixture.Mutation!.Value);
        Assert.Equal("uncertain", original.Value?.Status);
        var originalResult = await fixture.Proposals.FindResultAsync(proposal.Id);
        var forged = new AgentClientAcknowledgement(null!, proposal.PreparedInvocation!.ApprovalDigest,
            AgentClientOutcome.Applied, "Pretend this server invocation was a client request.", null, null);

        var rejected = await fixture.AcknowledgeAsync(proposal, forged);

        Assert.Equal("agent.invocation.client_unavailable", rejected.Error?.Code);
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
        Assert.Same(originalResult, await fixture.Proposals.FindResultAsync(proposal.Id));
        Assert.Same(original.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal("new", fixture.Mutation.Value);
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
    }

    [Fact]
    public async Task Cancellation_before_dispatch_preserves_approved_state_and_zero_effects()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Proposals.ExecuteAsync(proposal.Id, ProposalFixture.Actor, proposal.BaseRevision, cancelled.Token));
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        Assert.Equal(AgentActionProposalStatus.Approved, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
    }

    [Fact]
    public async Task Client_dispatch_is_pending_until_the_same_tool_admits_an_observed_receipt()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var pending = await fixture.ExecuteAsync(proposal);
        Assert.False(pending.Value!.Executed);
        Assert.Equal("awaiting-client", pending.Value.Status);
        Assert.Equal(proposal.PreparedInvocation!.InvocationId, pending.Value.ClientInvocation!.InvocationId);
        Assert.Null(await fixture.Proposals.FindResultAsync(proposal.Id));
        Assert.Equal(AgentActionProposalStatus.AwaitingClient, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
        var accepted = await fixture.AcknowledgeAsync(proposal);
        Assert.True(accepted.Value?.Executed);
        Assert.Equal(AgentEffectScope.ClientWorkingState, accepted.Value!.ToolResult!.Receipt!.Scope);
        Assert.False(accepted.Value.ToolResult.Payload!.Data.TryGetProperty("secret", out _));
        Assert.Equal(0, fixture.Client!.ExecuteCalls);
        Assert.Equal(1, fixture.Client.CompleteCalls);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("removed")]
    [InlineData("owner")]
    [InlineData("version")]
    [InlineData("location")]
    [InlineData("completion")]
    [InlineData("policy")]
    public async Task A_pending_client_retry_rechecks_current_authority_and_contract_before_reissuing_an_executable_request(string change)
    {
        var tool = new ProposalClientTool();
        var registry = new MutableProposalToolRegistry(tool);
        var denied = new List<string>();
        using var fixture = await ProposalFixture.CreateAsync(tool, AgentPolicy.Default with { DeniedCapabilityIds = denied },
            services => services.Replace(ServiceDescriptor.Scoped<IAgentToolRegistry>(_ => registry)));
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var first = await fixture.ExecuteAsync(proposal);
        Assert.Equal("awaiting-client", first.Value?.Status);
        Assert.NotNull(first.Value!.ClientInvocation);
        switch (change)
        {
            case "permission": fixture.Authority.Allowed = false; break;
            case "removed": registry.Current = null; break;
            case "owner": tool.Descriptor = tool.Descriptor with { ModuleId = "another.module" }; break;
            case "version": tool.Descriptor = tool.Descriptor with { ContractVersion = "2" }; break;
            case "location": tool.Descriptor = tool.Descriptor with { ExecutionLocation = AgentExecutionLocation.Server }; break;
            case "completion": registry.Current = new ProposalClientToolWithoutCompletion(tool); break;
            case "policy": denied.Add(tool.Descriptor.Name); break;
        }

        var retry = await fixture.ExecuteAsync(proposal);

        Assert.Equal("uncertain", retry.Value?.Status);
        Assert.False(retry.Value!.Executed);
        Assert.Null(retry.Value.ClientInvocation);
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
        Assert.Same(retry.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal(0, tool.ExecuteCalls);
        Assert.Equal(0, tool.CompleteCalls);
    }

    [Fact]
    public async Task An_authorized_pending_retry_reuses_the_envelope_without_calling_a_replacement_executor_again()
    {
        TemporarilyUnavailableCompletionExecutor? replacement = null;
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool(), configure: services =>
        {
            services.AddScoped<DefaultAgentActionProposalExecutor>();
            services.Replace(ServiceDescriptor.Scoped<IAgentActionProposalExecutor>(provider =>
                replacement = new(provider.GetRequiredService<DefaultAgentActionProposalExecutor>())));
        });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var first = await fixture.ExecuteAsync(proposal);

        var duplicate = await fixture.ExecuteAsync(proposal);

        Assert.Same(first.Value, duplicate.Value);
        Assert.NotNull(duplicate.Value!.ClientInvocation);
        Assert.Equal(1, replacement!.ExecuteCalls);
        Assert.Equal(0, fixture.Client!.ExecuteCalls);
    }

    [Fact]
    public async Task A_completed_receipt_duplicate_remains_coalesced_after_permission_or_module_changes()
    {
        var tool = new ProposalClientTool();
        var registry = new MutableProposalToolRegistry(tool);
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
            services.Replace(ServiceDescriptor.Scoped<IAgentToolRegistry>(_ => registry)));
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        await fixture.ExecuteAsync(proposal);
        var applied = await fixture.AcknowledgeAsync(proposal);
        fixture.Authority.Allowed = false;
        registry.Current = null;

        var duplicate = await fixture.ExecuteAsync(proposal);

        Assert.Same(applied.Value, duplicate.Value);
        Assert.True(duplicate.Value!.Executed);
        Assert.Null(duplicate.Value.ClientInvocation);
        Assert.Equal(1, tool.CompleteCalls);
        Assert.Equal(0, tool.ExecuteCalls);
    }

    [Fact]
    public async Task Duplicate_acknowledgement_is_idempotent_but_a_conflict_cannot_replace_its_receipt()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        await fixture.ExecuteAsync(proposal);
        var acknowledgement = fixture.Acknowledgement(proposal);
        var accepted = await fixture.AcknowledgeAsync(proposal, acknowledgement);
        var duplicate = await fixture.AcknowledgeAsync(proposal, acknowledgement);
        var conflict = await fixture.AcknowledgeAsync(proposal, acknowledgement with { Summary = "conflicting delivery" });
        Assert.Same(accepted.Value, duplicate.Value);
        Assert.Equal("agent.invocation.acknowledgement_conflict", conflict.Error?.Code);
        Assert.Equal(1, fixture.Client!.CompleteCalls);
        Assert.Same(accepted.Value!.ToolResult, await fixture.Proposals.FindResultAsync(proposal.Id));
    }

    [Fact]
    public async Task Mismatched_generation_session_actor_or_digest_never_reaches_completion()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        await fixture.ExecuteAsync(proposal);
        var prepared = proposal.PreparedInvocation!;
        var acknowledgement = fixture.Acknowledgement(proposal);
        Assert.Equal("agent.invocation.binding_mismatch", (await fixture.AcknowledgeAsync(proposal, acknowledgement with { Client = new("client-1", "replacement") })).Error?.Code);
        Assert.Equal("agent.invocation.binding_mismatch", (await fixture.AcknowledgeAsync(proposal, acknowledgement with { ApprovalDigest = "forged" })).Error?.Code);
        Assert.Equal("agent.invocation.outcome_invalid", (await fixture.AcknowledgeAsync(proposal, acknowledgement with { Outcome = (AgentClientOutcome)999 })).Error?.Code);
        Assert.Equal("agent.invocation.binding_mismatch", (await fixture.Proposals.AcknowledgeAsync("other-session", prepared.InvocationId, ProposalFixture.Actor, acknowledgement)).Error?.Code);
        Assert.Equal("agent.proposal.forbidden", (await fixture.Proposals.AcknowledgeAsync(fixture.Session.Id, prepared.InvocationId, "other-actor", acknowledgement)).Error?.Code);
        Assert.Equal(0, fixture.Client!.CompleteCalls);
        Assert.True((await fixture.AcknowledgeAsync(proposal)).Value?.Executed);
    }

    [Fact]
    public async Task An_unvalidated_report_does_not_replace_or_prevent_a_later_valid_receipt()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        await fixture.ExecuteAsync(proposal);
        var valid = fixture.Acknowledgement(proposal);
        var invalid = valid with { After = valid.After! with { ResourceId = "another-editor" } };
        var unverified = await fixture.AcknowledgeAsync(proposal, invalid);
        Assert.Equal("uncertain", unverified.Value?.Status);
        Assert.False(unverified.Value!.Executed);
        var accepted = await fixture.AcknowledgeAsync(proposal, valid);
        Assert.True(accepted.Value?.Executed);
        Assert.Equal(0, fixture.Client!.ExecuteCalls);
        Assert.Equal(2, fixture.Client.CompleteCalls);
    }

    [Fact]
    public async Task A_dispatched_client_tool_losing_completion_stays_uncertain_until_its_counterpart_returns()
    {
        var tool = new ProposalClientTool();
        var registry = new MutableProposalToolRegistry(tool);
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
            services.Replace(ServiceDescriptor.Scoped<IAgentToolRegistry>(_ => registry)));
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        Assert.Equal("awaiting-client", (await fixture.ExecuteAsync(proposal)).Value?.Status);
        var missingCompletion = new ProposalClientToolWithoutCompletion(tool);
        registry.Current = missingCompletion;

        var unresolved = await fixture.AcknowledgeAsync(proposal);

        Assert.True(unresolved.Succeeded);
        Assert.Equal("uncertain", unresolved.Value?.Status);
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
        Assert.Equal(0, tool.CompleteCalls);
        Assert.Same(unresolved.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal(0, missingCompletion.PrepareCalls);
        Assert.Equal(0, missingCompletion.ExecuteCalls);

        registry.Current = tool;
        var accepted = await fixture.AcknowledgeAsync(proposal);
        Assert.True(accepted.Value?.Executed);
        Assert.Equal(AgentActionProposalStatus.Executed, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
        Assert.Equal(1, tool.CompleteCalls);
        Assert.Equal(0, tool.ExecuteCalls);
        Assert.Same(accepted.Value!.ToolResult, await fixture.Proposals.FindResultAsync(proposal.Id));
    }

    [Theory]
    [InlineData(AgentClientOutcome.Applied)]
    [InlineData(AgentClientOutcome.Refused)]
    [InlineData(AgentClientOutcome.Uncertain)]
    public async Task A_typed_completion_executor_failure_preserves_later_evidence_without_redispatch(AgentClientOutcome reported)
    {
        TemporarilyUnavailableCompletionExecutor? replacement = null;
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool(), configure: services =>
        {
            services.AddScoped<DefaultAgentActionProposalExecutor>();
            services.Replace(ServiceDescriptor.Scoped<IAgentActionProposalExecutor>(provider =>
                replacement = new(provider.GetRequiredService<DefaultAgentActionProposalExecutor>())));
        });
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        Assert.Equal("awaiting-client", (await fixture.ExecuteAsync(proposal)).Value?.Status);

        var unresolved = await fixture.AcknowledgeAsync(proposal, fixture.Acknowledgement(proposal) with { Outcome = reported });

        Assert.True(unresolved.Succeeded);
        Assert.Equal("uncertain", unresolved.Value?.Status);
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await fixture.Proposals.FindAsync(proposal.Id))?.Status);
        Assert.Same(unresolved.Value, (await fixture.ExecuteAsync(proposal)).Value);
        Assert.Equal(0, fixture.Client!.CompleteCalls);
        replacement!.Unavailable = false;
        var accepted = await fixture.AcknowledgeAsync(proposal);
        Assert.True(accepted.Value?.Executed);
        Assert.Equal(1, fixture.Client.CompleteCalls);
        Assert.Equal(0, fixture.Client.ExecuteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_or_revoked_client_completion_preserves_uncertainty_without_readmitting_payload(bool revoked)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        await fixture.ExecuteAsync(proposal);
        if (revoked) fixture.Authority.Allowed = false;
        else fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var result = await fixture.AcknowledgeAsync(proposal);
        Assert.Equal("uncertain", result.Value?.Status);
        Assert.False(result.Value!.Executed);
        Assert.Equal(0, fixture.Client!.CompleteCalls);
        Assert.Equal("agent.operation-outcome", result.Value.ToolResult!.Payload!.TypeId);
    }

    [Fact]
    public async Task Denial_after_client_dispatch_does_not_claim_rollback_or_reopen_execution()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        await fixture.ExecuteAsync(proposal);
        var denied = await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Cancel");
        Assert.Equal(AgentActionProposalStatus.Uncertain, denied.Value?.Status);
        var retained = await fixture.ExecuteAsync(proposal);
        Assert.Equal("uncertain", retained.Value?.Status);
        Assert.Equal(0, fixture.Client!.ExecuteCalls);
    }

    [Fact]
    public async Task Proposal_without_a_prepared_executor_is_explicitly_unavailable()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var now = DateTimeOffset.UtcNow;
        var proposal = await fixture.Proposals.AddAsync(new("legacy", fixture.Session.Id, null, "legacy", "legacy", "Legacy", "No executor", AgentRisk.ReviewRequired,
            null, [], [], [], null, [], null, null, false, AgentActionProposalStatus.Approved, null, null, now, now));
        var result = await fixture.Proposals.ExecuteAsync(proposal.Id, ProposalFixture.Actor);
        Assert.Equal("agent.proposal.executor_unavailable", result.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
    }

    [Fact]
    public async Task Denied_operation_cannot_execute_and_completed_operation_cannot_be_denied()
    {
        using var deniedFixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var deniedProposal = await deniedFixture.ProposeAsync();
        Assert.True((await deniedFixture.Proposals.DenyAsync(deniedProposal.Id, ProposalFixture.Actor, "No")).Succeeded);
        Assert.Equal("agent.proposal.closed", (await deniedFixture.ExecuteAsync(deniedProposal)).Error?.Code);
        Assert.Equal(0, deniedFixture.Mutation!.ExecuteCalls);

        using var executedFixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var executedProposal = await executedFixture.ProposeAsync();
        await executedFixture.ApproveAsync(executedProposal);
        await executedFixture.ExecuteAsync(executedProposal);
        Assert.Equal("agent.proposal.closed", (await executedFixture.Proposals.DenyAsync(executedProposal.Id, ProposalFixture.Actor, "Too late")).Error?.Code);
    }

    [Fact]
    public async Task Draft_proposal_is_not_eligible_for_approval()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var now = DateTimeOffset.UtcNow;
        var draft = await fixture.Proposals.AddAsync(new("draft", fixture.Session.Id, null, "draft", "draft", "Draft", "Unprepared draft", AgentRisk.ReviewRequired,
            null, [], [], [], null, [], null, null, true, AgentActionProposalStatus.Draft, null, null, now, now));
        var result = await fixture.Proposals.ApproveAsync(draft.Id, ProposalFixture.Actor);
        Assert.Equal("agent.proposal.invalid_state", result.Error?.Code);
    }

    [Fact]
    public async Task Executor_failure_is_logged_without_its_secret_bearing_exception_text()
    {
        var logger = new RecordingLogger<InMemoryAgentProposalService>();
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool { FailAfterEffect = true },
            configure: services => services.Replace(ServiceDescriptor.Scoped<ILogger<InMemoryAgentProposalService>>(_ => logger)));
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        await fixture.ExecuteAsync(proposal);
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Null(error.Exception);
        Assert.DoesNotContain("secret-test-value", error.Message);
        Assert.Equal(nameof(InvalidOperationException), error.Fields["FailureType"]);
    }

    [Fact]
    public async Task Feedback_keeps_its_message_and_actor_audit_linkage()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        await fixture.Feedback.AddAsync(new("feedback-1", fixture.Session.Id, "message-1", "positive", "Helpful", ProposalFixture.Actor, DateTimeOffset.UtcNow));
        var received = Assert.Single(await fixture.Audit.ListAsync(fixture.Session.Id), x => x.Kind == AgentAuditEventKind.FeedbackReceived);
        Assert.Equal("message-1", received.Metadata["messageId"]);
        Assert.Equal("positive", received.Metadata["rating"]);
        Assert.Equal(ProposalFixture.Actor, received.ActorId);
    }
}

internal sealed class ProposalFixture : IDisposable
{
    public const string Actor = "actor-1";
    private readonly ServiceProvider _root;
    private readonly IServiceScope _scope;
    public IServiceProvider Services => _scope.ServiceProvider;
    public AgentSession Session { get; private set; } = null!;
    public ProposalTestAuthorizer Authority { get; } = new();
    public ProposalTestClock Clock { get; } = new();
    public IAgentTool Tool { get; }
    public ProposalMutationTool? Mutation => Tool as ProposalMutationTool;
    public ProposalClientTool? Client => Tool as ProposalClientTool;
    public IAgentProposalService Proposals => _scope.ServiceProvider.GetRequiredService<IAgentProposalService>();
    public IAgentToolInvoker Invoker => _scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>();
    public IAgentAuditReader Audit => _scope.ServiceProvider.GetRequiredService<IAgentAuditReader>();
    public IAgentFeedbackService Feedback => _scope.ServiceProvider.GetRequiredService<IAgentFeedbackService>();

    private ProposalFixture(IAgentTool tool, Action<IServiceCollection>? configure)
    {
        Tool = tool;
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddFoundationAgentAbstractions();
        services.AddScoped<IAgentTool>(_ => tool);
        services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => Authority));
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
        configure?.Invoke(services);
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _root.CreateScope();
    }

    public static async Task<ProposalFixture> CreateAsync(IAgentTool tool, AgentPolicy? policy = null, Action<IServiceCollection>? configure = null)
    {
        var fixture = new ProposalFixture(tool, configure);
        fixture.Session = await fixture._scope.ServiceProvider.GetRequiredService<IAgentSessionService>().CreateAsync(new(
            "tenant-1", Actor, "conversation-1", "test-provider", "explain", null, policy ?? AgentPolicy.Default, new Dictionary<string, string>()));
        return fixture;
    }

    public AgentToolInvocation Invocation(string callId = "call-1", IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var binding = new AgentClientBinding("client-1", "generation-1");
        IReadOnlyCollection<AgentContextAttachment> context = Tool.Descriptor.ExecutionLocation == AgentExecutionLocation.ClientWorkingState
            ? [new("snapshot-1", "test", "editor-1", "Editor", "test.editor", AgentContextSensitivity.Internal, "selection", "Bound editor", null, new Dictionary<string, string>())
            { ModuleId = "test.module", Origin = AgentContextOrigin.ClientSnapshot, Client = binding, Target = new("test.module", "test.editor", "editor-1", "revision-1") }]
            : [];
        return new(callId, Tool.Descriptor.Name, Session.Id, Actor,
            arguments ?? new Dictionary<string, object?> { ["value"] = "new", ["text"] = "hi" }, context)
        { TenantId = Session.TenantId, TurnId = "turn-1", Client = binding };
    }

    public async Task<AgentActionProposal> ProposeAsync()
    {
        var dispatch = await Invoker.DispatchAsync(Invocation(), Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        return dispatch.Proposal!;
    }
    public Task<AgentResult<AgentActionProposal>> ApproveAsync(AgentActionProposal proposal) => Proposals.ApproveAsync(proposal.Id, Actor, proposal.BaseRevision);
    public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal) => Proposals.ExecuteAsync(proposal.Id, Actor, proposal.BaseRevision);
    public AgentClientAcknowledgement Acknowledgement(AgentActionProposal proposal) => new(proposal.PreparedInvocation!.Client!, proposal.PreparedInvocation.ApprovalDigest,
        AgentClientOutcome.Applied, "The client observed the edit.", proposal.ResourceTarget! with { Revision = "revision-2" },
        new("test.client-change", "1", JsonSerializer.SerializeToElement(new { observed = true, value = "new", revision = "revision-2", secret = "omit" })));
    public Task<AgentResult<AgentProposalExecutionResult>> AcknowledgeAsync(AgentActionProposal proposal, AgentClientAcknowledgement? acknowledgement = null)
        => Proposals.AcknowledgeAsync(Session.Id, proposal.PreparedInvocation!.InvocationId, Actor, acknowledgement ?? Acknowledgement(proposal));
    public void Dispose() { _scope.Dispose(); _root.Dispose(); }
}

internal sealed class ProposalTestAuthorizer : IAgentToolAuthorizer
{
    public bool Allowed { get; set; } = true;
    public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentPolicyDecision(Allowed, Allowed ? [] : [new("test.denied", "Denied")]));
}

internal sealed class ProposalTestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan value) => _now += value;
}

internal sealed class MutableProposalToolRegistry(IAgentTool tool) : IAgentToolRegistry
{
    public IAgentTool? Current { get; set; } = tool;
    public Task<IReadOnlyCollection<IAgentTool>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<IAgentTool>>(Current is null ? [] : [Current]);
    public Task<IAgentTool?> FindAsync(string toolName, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(Current?.Descriptor.Name, toolName, StringComparison.OrdinalIgnoreCase) ? Current : null);
}

internal sealed class ProposalClientToolWithoutCompletion(ProposalClientTool tool) : IAgentTool
{
    public int PrepareCalls { get; private set; }
    public int ExecuteCalls { get; private set; }
    public AgentToolDescriptor Descriptor => tool.Descriptor;
    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        PrepareCalls++;
        return tool.PrepareAsync(invocation, cancellationToken);
    }
    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        ExecuteCalls++;
        return tool.ExecuteAsync(invocation, cancellationToken);
    }
}

internal sealed class TemporarilyUnavailableCompletionExecutor(DefaultAgentActionProposalExecutor inner) : IAgentActionProposalExecutor
{
    public bool Unavailable { get; set; } = true;
    public int ExecuteCalls { get; private set; }
    public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
    {
        ExecuteCalls++;
        return inner.ExecuteAsync(proposal, cancellationToken);
    }
    public Task<AgentResult<AgentProposalExecutionResult>> CompleteClientAsync(AgentActionProposal proposal,
        AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
        => Unavailable
            ? Task.FromResult(AgentResult<AgentProposalExecutionResult>.Failure("test.completion_unavailable", "Completion validation is temporarily unavailable.", 409))
            : inner.CompleteClientAsync(proposal, acknowledgement, cancellationToken);
}

internal sealed class ProposalMutationTool : IAgentTool
{
    private int _revision = 1;
    public string Value { get; private set; } = "initial";
    public string Revision => $"revision-{_revision}";
    public int PrepareCalls { get; private set; }
    public int ExecuteCalls { get; private set; }
    public AgentRisk CurrentRisk { get; set; } = AgentRisk.ReviewRequired;
    public bool NoopWithoutReceipt { get; init; }
    public bool FailAfterEffect { get; init; }
    public bool CancelAfterEffect { get; init; }
    public bool Malformed { get; init; }
    public TaskCompletionSource? Started { get; init; }
    public TaskCompletionSource? Release { get; init; }
    public AgentToolDescriptor Descriptor => new("test_mutate", "Update resource", "Updates one owned test resource.", AgentToolMutability.Mutating,
        CurrentRisk, "{}", [], []) { ModuleId = "test.module", ResultType = "test.change", ResultSchemaVersion = "1" };
    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        PrepareCalls++;
        if (Malformed) return Task.FromResult(AgentResult<AgentToolPreparation>.Failure("test.malformed", "The module refused malformed arguments."));
        return Task.FromResult(AgentResult<AgentToolPreparation>.Success(new(new("test.module", "test.resource", "resource-1", Revision),
            JsonSerializer.SerializeToElement(invocation.Arguments), AgentRisk.ReviewRequired,
            new("test.preview", "1", JsonSerializer.SerializeToElement(new { before = Value, after = "new" })), false)));
    }
    public async Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.Target!.Revision != Revision) return AgentResult<AgentToolResult>.Failure("test.stale", "The resource changed after review.", 409);
        Started?.TrySetResult();
        if (Release is not null) await Release.Task.WaitAsync(cancellationToken);
        ExecuteCalls++;
        if (NoopWithoutReceipt) return AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "A misleading success."));
        Value = invocation.Arguments.GetProperty("value").GetString()!;
        _revision++;
        if (FailAfterEffect) throw new InvalidOperationException("secret-test-value");
        if (CancelAfterEffect) throw new OperationCanceledException("secret-test-value");
        var readback = new AgentPayload("test.change", "1", JsonSerializer.SerializeToElement(new { value = Value, revision = Revision }));
        return AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "The owning resource was updated.", readback)
        { Receipt = new(invocation.InvocationId, invocation.Target, invocation.Target with { Revision = Revision }, AgentEffectScope.ServerPersisted, readback) });
    }
    public void ChangeByUser(string value) { Value = value; _revision++; }
}

internal sealed class ProposalClientTool : IAgentTool, IAgentClientTool
{
    public int ExecuteCalls { get; private set; }
    public int CompleteCalls { get; private set; }
    public bool UseSnapshotBinding { get; set; }
    public AgentToolDescriptor Descriptor { get; set; } = new("test_client_change", "Change local resource", "Changes an originating local resource.", AgentToolMutability.Mutating,
        AgentRisk.ReviewRequired, "{}", [], []) { ModuleId = "test.module", ExecutionLocation = AgentExecutionLocation.ClientWorkingState };
    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentToolPreparation>.Success(new(new("test.module", "test.editor", "editor-1", "revision-1"),
            JsonSerializer.SerializeToElement(invocation.Arguments), AgentRisk.ReviewRequired,
            new("test.preview", "1", JsonSerializer.SerializeToElement(new { value = "new" })), false)
        { Client = UseSnapshotBinding ? invocation.Context.Single().Client : invocation.Client }));
    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    { ExecuteCalls++; return Task.FromResult(AgentResult<AgentToolResult>.Failure("test.client_only", "The browser owns this operation.")); }
    public Task<AgentResult<AgentToolResult>> CompleteAsync(AgentPreparedToolInvocation invocation, AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
    {
        CompleteCalls++;
        if (acknowledgement.After is not { } after || after.ModuleId != invocation.ModuleId || after.ResourceId != invocation.Target!.ResourceId
            || acknowledgement.Result is not { TypeId: "test.client-change", SchemaVersion: "1" } payload || !payload.Data.GetProperty("observed").GetBoolean())
            return Task.FromResult(AgentResult<AgentToolResult>.Failure("test.observation_invalid", "No valid observed outcome.", 409));
        var admitted = new AgentPayload("test.client-change", "1", JsonSerializer.SerializeToElement(new
        { value = payload.Data.GetProperty("value").GetString(), revision = after.Revision, observed = true }));
        return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "Observed local working-state edit.", admitted)
        { Receipt = new(invocation.InvocationId, invocation.Target!, after, AgentEffectScope.ClientWorkingState, admitted) }));
    }
}
