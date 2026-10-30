using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.Services;

namespace Elsa.Agent.Tests;

/// <summary>
/// Issue #414 item 6: duplicate tool names were silently dropped (first-registered won), inconsistent
/// with the loud failure on invalid names. Registration now fails fast, naming the offenders.
/// </summary>
public sealed class AgentToolRegistryTests
{
    [Fact]
    public async Task Distinct_tool_names_register_without_error()
    {
        var registry = new DefaultAgentToolRegistry(new AgentTestToolsPublisher(new EchoTool(), new ApplyChangeTool()));

        Assert.Equal(2, (await registry.ListAsync()).Count);
    }

    [Fact]
    public async Task Case_insensitive_duplicate_tool_names_fail_fast_and_name_the_offender()
    {
        // The lookup comparer is OrdinalIgnoreCase, so names differing only by case are duplicates too.
        var ex = await Assert.ThrowsAsync<AgentContributionException>(() =>
            new DefaultAgentToolRegistry(new AgentTestToolsPublisher(new EchoTool(), new UpperEchoTool())).ListAsync());

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
            []) { ModuleId = "test.module" };

        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolPreparation>.Success(AgentTestToolHelpers.Preparation(invocation, Descriptor)));

        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "echo")));
    }
}
