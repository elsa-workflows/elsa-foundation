using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Tests;

public sealed class AgentHarnessTests
{
    private readonly InMemoryAgentAuditStore _audit = new();

    [Fact]
    public async Task Read_only_tool_runs_when_invoked_as_an_ai_function()
    {
        var function = AgentToolAIFunctions.Create(new EchoTool(), Context(AgentPolicy.Default));

        Assert.Equal(DeterministicAgentProvider.ReadOnlyToolName, function.Name);
        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["text"] = "hi" }));

        Assert.Contains("echo:hi", result?.ToString());
    }

    [Fact]
    public async Task Mutating_tool_invoked_as_ai_function_creates_a_proposal_and_signals_pending_approval()
    {
        AgentActionProposal? surfaced = null;
        var function = AgentToolAIFunctions.Create(new ApplyChangeTool(), Context(AgentPolicy.Default, proposal => surfaced = proposal));

        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["change"] = "add" }));

        Assert.NotNull(surfaced);
        Assert.Contains("awaiting human approval", result?.ToString());
    }

    private AgentToolAIFunctionContext Context(AgentPolicy policy, Action<AgentActionProposal>? onProposal = null)
    {
        var tools = new IAgentTool[] { new EchoTool(), new ApplyChangeTool() };
        var proposals = new InMemoryAgentProposalService(new NoopAgentActionProposalExecutor(), _audit);
        var invoker = new DefaultAgentToolInvoker(new DefaultAgentToolRegistry(tools), proposals, _audit);
        return new AgentToolAIFunctionContext("session-1", "actor-1", [], policy, invoker, onProposal);
    }
}
