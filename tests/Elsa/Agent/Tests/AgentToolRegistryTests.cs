using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;

namespace Elsa.Agent.Tests;

/// <summary>
/// Issue #414 item 6: duplicate tool names were silently dropped (first-registered won), inconsistent
/// with the loud failure on invalid names. Registration now fails fast, naming the offenders.
/// </summary>
public sealed class AgentToolRegistryTests
{
    [Fact]
    public void Distinct_tool_names_register_without_error()
    {
        var registry = new DefaultAgentToolRegistry([new EchoTool(), new ApplyChangeTool()]);

        Assert.Equal(2, registry.Tools.Count);
    }

    [Fact]
    public void Case_insensitive_duplicate_tool_names_fail_fast_and_name_the_offender()
    {
        // The lookup comparer is OrdinalIgnoreCase, so names differing only by case are duplicates too.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new DefaultAgentToolRegistry([new EchoTool(), new UpperEchoTool()]));

        Assert.Contains("Duplicate agent tool name", ex.Message);
    }

    // Same descriptor name as EchoTool but upper-cased, to prove the guard is case-insensitive.
    private sealed class UpperEchoTool : IAgentTool
    {
        public AgentToolDescriptor Descriptor { get; } = new(
            DeterministicAgentProvider.ReadOnlyToolName.ToUpperInvariant(),
            "Echo",
            "Echoes the provided text.",
            AgentToolMutability.ReadOnly,
            AgentRisk.ReadOnly,
            "{}",
            [],
            []);

        public Task<AgentResult<AgentToolResult>> InvokeAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "echo")));
    }
}
