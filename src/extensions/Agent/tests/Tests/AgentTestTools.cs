using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;

namespace Elsa.Agent.Tests;

/// <summary>A read-only tool that echoes its <c>text</c> argument. Used to exercise inline tool execution.</summary>
internal sealed class EchoTool : IAgentTool
{
    public AgentToolDescriptor Descriptor { get; } = new(
        DeterministicAgentProvider.ReadOnlyToolName,
        "Echo",
        "Echoes the provided text.",
        AgentToolMutability.ReadOnly,
        AgentRisk.ReadOnly,
        """{"type":"object","properties":{"text":{"type":"string"}}}""",
        [],
        []);

    public Task<AgentResult<AgentToolResult>> InvokeAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var text = invocation.Arguments.TryGetValue("text", out var value) ? Convert.ToString(value) ?? string.Empty : string.Empty;
        return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, $"echo:{text}")));
    }
}

/// <summary>A mutating tool that records invocations. Used to exercise the proposal/approval path and full-auto.</summary>
internal sealed class ApplyChangeTool : IAgentTool
{
    public int Invocations { get; private set; }

    public AgentToolDescriptor Descriptor { get; } = new(
        DeterministicAgentProvider.MutatingToolName,
        "Apply change",
        "Applies a workflow change.",
        AgentToolMutability.Mutating,
        AgentRisk.ReviewRequired,
        """{"type":"object","properties":{"change":{"type":"string"}}}""",
        [],
        []);

    public Task<AgentResult<AgentToolResult>> InvokeAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        Invocations++;
        return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "applied")));
    }
}

/// <summary>A read-only tool that throws, to verify the invoker wraps exceptions as a denial.</summary>
internal sealed class ThrowingTool : IAgentTool
{
    public AgentToolDescriptor Descriptor { get; } = new(
        "boom",
        "Boom",
        "Always throws.",
        AgentToolMutability.ReadOnly,
        AgentRisk.ReadOnly,
        "{}",
        [],
        []);

    public Task<AgentResult<AgentToolResult>> InvokeAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("tool exploded");
}

internal static class AgentTestToolHelpers
{
    public static AgentToolInvocation Invocation(string toolName, string sessionId = "session-1")
        => new($"{toolName}-call", toolName, sessionId, "actor-1", new Dictionary<string, object?> { ["text"] = "hi" }, []);
}
