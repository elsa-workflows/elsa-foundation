using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentToolInvokerTests
{
    [Fact]
    public async Task Read_only_tool_executes_inline_with_its_typed_result()
    {
        using var fixture = await ProposalFixture.CreateAsync(new EchoTool());
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        Assert.Equal("echo:hi", dispatch.Result?.Content);
        Assert.Equal("test.echo", dispatch.Result?.Payload?.TypeId);
        Assert.Null(dispatch.Result?.Receipt);
        var audited = Assert.Single(await fixture.Audit.ListAsync(fixture.Session.Id), x => x.Kind == AgentAuditEventKind.ToolInvoked);
        Assert.Equal("executed", audited.Metadata["outcome"]);
        Assert.Equal(fixture.Tool.Descriptor.Name, audited.Metadata["toolName"]);
    }

    [Theory]
    [InlineData(AgentAutonomyMode.Manual)]
    [InlineData(AgentAutonomyMode.AutoReadOnly)]
    public async Task Mutating_tool_prepares_an_exact_target_before_review(AgentAutonomyMode autonomy)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), AgentPolicy.Default with { AutonomyMode = autonomy });
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, dispatch.Proposal?.Status);
        Assert.Equal("test.resource", dispatch.Proposal?.ResourceTarget?.ResourceType);
        Assert.Equal("revision-1", dispatch.Proposal?.BaseRevision);
        Assert.Equal("new", dispatch.Proposal!.PreparedInvocation!.Arguments.GetProperty("value").GetString());
        Assert.Equal(1, fixture.Mutation!.PrepareCalls);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Fact]
    public async Task Malformed_module_preparation_never_creates_a_review_or_effect()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool { Malformed = true });
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal("test.malformed", dispatch.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        Assert.DoesNotContain(await fixture.Audit.ListAsync(fixture.Session.Id), x => x.Kind == AgentAuditEventKind.ProposalCreated);
    }

    [Fact]
    public async Task Server_mutation_under_full_auto_uses_the_same_observed_receipt_path()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto });
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        Assert.Equal("new", fixture.Mutation!.Value);
        Assert.Equal(fixture.Mutation.Revision, dispatch.Result?.Receipt?.After.Revision);
    }

    [Fact]
    public async Task Client_mutation_requires_explicit_review_even_in_full_auto()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool(), AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto });
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        Assert.True(dispatch.Proposal!.RequiresApproval);
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, dispatch.Proposal.Status);
        Assert.Equal(0, fixture.Client!.ExecuteCalls);
    }

    [Fact]
    public async Task Repeated_mutating_call_returns_its_receipt_without_repreparing_the_changed_resource()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto });
        var invocation = fixture.Invocation();
        var first = await fixture.Invoker.DispatchAsync(invocation, fixture.Session.Policy);
        var repeated = await fixture.Invoker.DispatchAsync(invocation, fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.Executed, repeated.Outcome);
        Assert.Same(first.Result, repeated.Result);
        Assert.Equal(1, fixture.Mutation!.ExecuteCalls);
        Assert.Equal(1, fixture.Mutation.PrepareCalls);
        var conflicting = await fixture.Invoker.DispatchAsync(invocation with { Arguments = new Dictionary<string, object?> { ["value"] = "other" } }, fixture.Session.Policy);
        Assert.Equal("agent.tool.duplicate_conflict", conflicting.Error?.Code);
        Assert.Equal(1, fixture.Mutation.ExecuteCalls);
    }

    [Fact]
    public async Task Receipt_continuation_cannot_authorize_a_second_mutation()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto });
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation() with { AllowMutations = false }, fixture.Session.Policy);
        Assert.Equal("agent.tool.continuation_read_only", dispatch.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.PrepareCalls);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
    }

    [Fact]
    public async Task Additional_distinct_mutator_in_the_bounded_turn_is_explicitly_refused()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var first = await fixture.Invoker.DispatchAsync(fixture.Invocation("first"), fixture.Session.Policy);
        var second = await fixture.Invoker.DispatchAsync(fixture.Invocation("second"), fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, first.Outcome);
        Assert.Equal("agent.tool.turn_mutation_limit", second.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        var proposals = (await fixture.Audit.ListAsync(fixture.Session.Id)).Where(x => x.Kind == AgentAuditEventKind.ProposalCreated);
        Assert.Single(proposals);
    }

    [Fact]
    public async Task A_module_snapshot_binds_client_execution_without_a_global_first_module_binding()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalClientTool());
        var invocation = fixture.Invocation() with { Client = null };
        var snapshotBinding = invocation.Context.Single().Client;
        fixture.Client!.UseSnapshotBinding = true;
        var dispatch = await fixture.Invoker.DispatchAsync(invocation, fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        Assert.Equal(snapshotBinding, dispatch.Proposal?.PreparedInvocation?.Client);
    }

    [Fact]
    public async Task Wrong_actor_or_tenant_never_reaches_module_preparation()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        var invocation = fixture.Invocation();
        Assert.Equal("agent.tool.forbidden", (await fixture.Invoker.DispatchAsync(invocation with { ActorId = "other" }, fixture.Session.Policy)).Error?.Code);
        Assert.Equal("agent.tool.forbidden", (await fixture.Invoker.DispatchAsync(invocation with { TenantId = "other" }, fixture.Session.Policy)).Error?.Code);
        Assert.Equal(0, fixture.Mutation!.PrepareCalls);
    }

    [Fact]
    public async Task Current_denial_blocks_effects_and_review_creation()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool());
        fixture.Authority.Allowed = false;
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal("agent.tool.forbidden", dispatch.Error?.Code);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        Assert.DoesNotContain(await fixture.Audit.ListAsync(fixture.Session.Id), x => x.Kind == AgentAuditEventKind.ProposalCreated);
    }

    [Fact]
    public async Task Unknown_tool_is_explicitly_denied()
    {
        using var fixture = await ProposalFixture.CreateAsync(new EchoTool());
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation() with { ToolName = "absent" }, fixture.Session.Policy);
        Assert.Equal("agent.tool.not_found", dispatch.Error?.Code);
    }

    [Fact]
    public async Task Read_exception_is_a_refusal_without_exposing_infrastructure_details()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ThrowingTool());
        var dispatch = await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        Assert.Equal("agent.tool.invocation_failed", dispatch.Error?.Code);
        Assert.DoesNotContain("tool exploded", dispatch.Error?.Message);
    }

    [Fact]
    public async Task Generic_defaults_fail_closed_without_a_current_authorizer()
    {
        var authorizer = new UnavailableAgentToolAuthorizer();
        var result = await authorizer.AuthorizeAsync(new EchoTool().Descriptor, "actor-1", "tenant-1", null);
        Assert.False(result.Allowed);
        Assert.Equal("agent.authority.unavailable", Assert.Single(result.Violations).Code);
    }
}
