using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;

namespace Elsa.Agent.Tests;

public sealed class AgentToolInvokerTests
{
    private static readonly AgentPolicy ApprovalPolicy = AgentPolicy.Default; // RequireProposalApproval = true
    private static readonly AgentPolicy FullAutoPolicy = AgentPolicy.Default with { RequireProposalApproval = false };

    private readonly InMemoryAgentAuditStore _audit = new();

    [Fact]
    public async Task Read_only_tool_executes_inline()
    {
        var invoker = BuildInvoker(out _, new EchoTool());

        var dispatch = await invoker.DispatchAsync(AgentTestToolHelpers.Invocation(DeterministicAgentProvider.ReadOnlyToolName), ApprovalPolicy);

        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        Assert.Equal("echo:hi", dispatch.Result?.Content);
    }

    [Fact]
    public async Task Mutating_tool_becomes_proposal_when_policy_requires_approval()
    {
        var invoker = BuildInvoker(out var proposals, new ApplyChangeTool());

        var dispatch = await invoker.DispatchAsync(AgentTestToolHelpers.Invocation(DeterministicAgentProvider.MutatingToolName), ApprovalPolicy);

        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        Assert.NotNull(dispatch.Proposal);
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, dispatch.Proposal!.Status);
        Assert.NotNull(await proposals.FindAsync(dispatch.Proposal.Id));
    }

    [Fact]
    public async Task Mutating_tool_executes_inline_under_full_auto_policy()
    {
        var tool = new ApplyChangeTool();
        var invoker = BuildInvoker(out _, tool);

        var dispatch = await invoker.DispatchAsync(AgentTestToolHelpers.Invocation(DeterministicAgentProvider.MutatingToolName), FullAutoPolicy);

        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        Assert.Equal(1, tool.Invocations);
    }

    [Fact]
    public async Task Unknown_tool_is_denied()
    {
        var invoker = BuildInvoker(out _);

        var dispatch = await invoker.DispatchAsync(AgentTestToolHelpers.Invocation("missing"), ApprovalPolicy);

        Assert.Equal(AgentToolDispatchOutcome.Denied, dispatch.Outcome);
        Assert.Equal("agent.tool.not_found", dispatch.Error?.Code);
    }

    [Fact]
    public async Task Tool_exception_is_wrapped_as_denial()
    {
        var invoker = BuildInvoker(out _, new ThrowingTool());

        var dispatch = await invoker.DispatchAsync(AgentTestToolHelpers.Invocation("boom"), ApprovalPolicy);

        Assert.Equal(AgentToolDispatchOutcome.Denied, dispatch.Outcome);
        Assert.Equal("agent.tool.invocation_failed", dispatch.Error?.Code);
    }

    [Fact]
    public async Task Tool_invocation_is_audited()
    {
        var invoker = BuildInvoker(out _, new EchoTool());

        await invoker.DispatchAsync(AgentTestToolHelpers.Invocation(DeterministicAgentProvider.ReadOnlyToolName), ApprovalPolicy);

        var auditEvent = Assert.Single(await _audit.ListAsync("session-1"), x => x.Kind == AgentAuditEventKind.ToolInvoked);
        Assert.Equal("executed", auditEvent.Metadata["outcome"]);
        Assert.Equal(DeterministicAgentProvider.ReadOnlyToolName, auditEvent.Metadata["toolName"]);
    }

    private DefaultAgentToolInvoker BuildInvoker(out IAgentProposalService proposals, params IAgentTool[] tools)
    {
        proposals = new InMemoryAgentProposalService(new NoopAgentActionProposalExecutor(), _audit);
        return new DefaultAgentToolInvoker(new DefaultAgentToolRegistry(tools), proposals, _audit);
    }
}
