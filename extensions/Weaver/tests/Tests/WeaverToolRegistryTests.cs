using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;
using Elsa.Weaver.Services;

namespace Elsa.Weaver.Tests;

/// <summary>
/// Issue #414 item 6: duplicate tool names were silently dropped (first-registered won), inconsistent
/// with the loud failure on invalid names. Registration now fails fast, naming the offenders.
/// </summary>
public sealed class WeaverToolRegistryTests
{
    [Fact]
    public async Task Distinct_tool_names_register_without_error()
    {
        var registry = new DefaultWeaverToolRegistry(new WeaverTestToolsPublisher(new EchoTool(), new ApplyChangeTool()));

        Assert.Equal(2, (await registry.ListAsync()).Count);
    }

    [Fact]
    public async Task Case_insensitive_duplicate_tool_names_fail_fast_and_name_the_offender()
    {
        // The lookup comparer is OrdinalIgnoreCase, so names differing only by case are duplicates too.
        var ex = await Assert.ThrowsAsync<WeaverContributionException>(() =>
            new DefaultWeaverToolRegistry(new WeaverTestToolsPublisher(new EchoTool(), new UpperEchoTool())).ListAsync());

        Assert.Contains("Duplicate Weaver tool name", ex.Message);
    }

    // Same descriptor name as EchoTool but upper-cased, to prove the guard is case-insensitive.
    private sealed class UpperEchoTool : IWeaverTool
    {
        public WeaverToolDescriptor Descriptor { get; } = new(
            DeterministicWeaverProvider.ReadOnlyToolName.ToUpperInvariant(),
            "Echo",
            "Echoes the provided text.",
            WeaverToolMutability.ReadOnly,
            WeaverRisk.ReadOnly,
            "{}",
            [],
            []) { ModuleId = "test.module" };

        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(WeaverTestToolHelpers.Preparation(invocation, Descriptor)));

        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "echo")));
    }
}
