using System.Text.Json;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Agent.Tests;

internal sealed class EchoTool : IAgentTool
{
    public AgentToolDescriptor Descriptor { get; } = new(DeterministicAgentProvider.ReadOnlyToolName, "Echo", "Echoes text.",
        AgentToolMutability.ReadOnly, AgentRisk.ReadOnly, """{"type":"object","properties":{"text":{"type":"string"}}}""", [], [])
    { ModuleId = "test.module" };
    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor)));
    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var text = invocation.Arguments.TryGetProperty("text", out var value) ? value.ToString() : string.Empty;
        return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, $"echo:{text}",
            new("test.echo", "1", JsonSerializer.SerializeToElement(new { text })))));
    }
}

internal sealed class ApplyChangeTool : IAgentTool
{
    public int Invocations { get; private set; }
    public AgentToolDescriptor Descriptor { get; } = new(DeterministicAgentProvider.MutatingToolName, "Apply change", "Applies a test resource change.",
        AgentToolMutability.Mutating, AgentRisk.ReviewRequired, """{"type":"object","properties":{"change":{"type":"string"}}}""", [], [])
    { ModuleId = "test.module" };
    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor) with
        { Target = new("test.module", "test.resource", "resource-1", "revision-1") }));
    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        Invocations++;
        var data = new AgentPayload("test.change", "1", JsonSerializer.SerializeToElement(new { invocations = Invocations }));
        return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "applied", data)
        { Receipt = new(invocation.InvocationId, invocation.Target!, invocation.Target! with { Revision = $"revision-{Invocations + 1}" }, AgentEffectScope.ServerPersisted, data) }));
    }
}

internal sealed class ThrowingTool : IAgentTool
{
    public AgentToolDescriptor Descriptor { get; } = new("boom", "Boom", "Always throws.", AgentToolMutability.ReadOnly, AgentRisk.ReadOnly, "{}", [], [])
    { ModuleId = "test.module" };
    public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor)));
    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("tool exploded");
}

internal static class AgentTestToolHelpers
{
    public static AgentToolInvocation Invocation(string toolName, string sessionId = "session-1")
        => new($"{toolName}-call", toolName, sessionId, "actor-1", new Dictionary<string, object?> { ["text"] = "hi" }, [])
        { TenantId = "tenant-1", TurnId = "turn-1" };

    public static AgentToolPreparation Preparation(AgentToolInvocation invocation, AgentToolDescriptor descriptor) =>
        new(null, JsonSerializer.SerializeToElement(invocation.Arguments), descriptor.Risk,
            new("test.preview", "1", JsonSerializer.SerializeToElement(new { tool = descriptor.Name })), RequiresApproval: false);

    public static AgentPreparedToolInvocation Prepared(AgentToolInvocation invocation, AgentToolDescriptor descriptor, AgentToolPreparation preparation) =>
        new("invocation-1", invocation.SessionId, invocation.TurnId, invocation.ToolCallId, descriptor.Name, descriptor.ContractVersion,
            descriptor.ModuleId, invocation.ActorId, invocation.TenantId, preparation.Target, preparation.Arguments, "test-digest", preparation.Risk,
            preparation.Preview, descriptor.ExecutionLocation, preparation.RequiresApproval, preparation.Client, DateTimeOffset.UtcNow.AddMinutes(5));
}

internal sealed class AgentTestToolsPublisher(params IAgentTool[] tools) : IInlineEventPublisher
{
    public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event is AgentToolsCollecting collecting)
            foreach (var tool in tools) collecting.Tools.Add(tool);
        return Task.CompletedTask;
    }
}

internal sealed class AgentTestToolInvoker(Func<AgentToolInvocation, AgentPolicy, Task<AgentToolDispatch>>? dispatch = null) : IAgentToolInvoker
{
    public Task<AgentToolDispatch> DispatchAsync(AgentToolInvocation invocation, AgentPolicy policy, CancellationToken cancellationToken = default)
        => dispatch?.Invoke(invocation, policy) ?? Task.FromResult(AgentToolDispatch.Denied(new("test.not_configured", "No tool response configured.")));
}
