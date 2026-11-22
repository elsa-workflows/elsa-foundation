using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;
using Microsoft.Extensions.AI;
using Elsa.Weaver.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

public sealed class WeaverHarnessTests
{
    [Fact]
    public async Task Read_only_tool_runs_when_invoked_as_an_ai_function()
    {
        using var fixture = await ContextAsync(WeaverPolicy.Default);
        var function = WeaverToolAIFunctions.Create(new EchoTool(), fixture.Context);

        Assert.Equal(DeterministicWeaverProvider.ReadOnlyToolName, function.Name);
        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["text"] = "hi" }));

        Assert.Contains("echo:hi", result?.ToString());
    }

    [Fact]
    public async Task Mutating_tool_invoked_as_ai_function_creates_a_proposal_and_signals_pending_approval()
    {
        WeaverActionProposal? surfaced = null;
        using var fixture = await ContextAsync(WeaverPolicy.Default, proposal => surfaced = proposal);
        var function = WeaverToolAIFunctions.Create(new ApplyChangeTool(), fixture.Context);

        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["change"] = "add" }));

        Assert.NotNull(surfaced);
        Assert.Contains("awaiting human approval", result?.ToString());
    }

    private static async Task<ToolFixture> ContextAsync(WeaverPolicy policy, Action<WeaverActionProposal>? onProposal = null)
    {
        var root = WeaverTestServices.Create(services =>
        {
            services.AddScoped<IWeaverTool, EchoTool>();
            services.AddScoped<IWeaverTool, ApplyChangeTool>();
        });
        var scope = root.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<IWeaverSessionService>().CreateAsync(new(
            "tenant-1", "actor-1", "conversation-1", "test", "explain", null, policy, new Dictionary<string, string>()));
        return new(root, scope, new(session.Id, session.ActorId, [], policy, scope.ServiceProvider.GetRequiredService<IWeaverToolInvoker>(), onProposal)
        { TenantId = session.TenantId, TurnId = "turn-1" });
    }

    private sealed class ToolFixture(ServiceProvider root, IServiceScope scope, WeaverToolAIFunctionContext context) : IDisposable
    {
        public WeaverToolAIFunctionContext Context { get; } = context;
        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }
}
