using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Microsoft.Extensions.AI;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentHarnessTests
{
    [Fact]
    public async Task Read_only_tool_runs_when_invoked_as_an_ai_function()
    {
        using var fixture = await ContextAsync(AgentPolicy.Default);
        var function = AgentToolAIFunctions.Create(new EchoTool(), fixture.Context);

        Assert.Equal(DeterministicAgentProvider.ReadOnlyToolName, function.Name);
        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["text"] = "hi" }));

        Assert.Contains("echo:hi", result?.ToString());
    }

    [Fact]
    public async Task Mutating_tool_invoked_as_ai_function_creates_a_proposal_and_signals_pending_approval()
    {
        AgentActionProposal? surfaced = null;
        using var fixture = await ContextAsync(AgentPolicy.Default, proposal => surfaced = proposal);
        var function = AgentToolAIFunctions.Create(new ApplyChangeTool(), fixture.Context);

        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["change"] = "add" }));

        Assert.NotNull(surfaced);
        Assert.Contains("awaiting human approval", result?.ToString());
    }

    private static async Task<ToolFixture> ContextAsync(AgentPolicy policy, Action<AgentActionProposal>? onProposal = null)
    {
        var root = AgentTestServices.Create(services =>
        {
            services.AddScoped<IAgentTool, EchoTool>();
            services.AddScoped<IAgentTool, ApplyChangeTool>();
        });
        var scope = root.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<IAgentSessionService>().CreateAsync(new(
            "tenant-1", "actor-1", "conversation-1", "test", "explain", null, policy, new Dictionary<string, string>()));
        return new(root, scope, new(session.Id, session.ActorId, [], policy, scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>(), onProposal)
        { TenantId = session.TenantId, TurnId = "turn-1" });
    }

    private sealed class ToolFixture(ServiceProvider root, IServiceScope scope, AgentToolAIFunctionContext context) : IDisposable
    {
        public AgentToolAIFunctionContext Context { get; } = context;
        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }
}
