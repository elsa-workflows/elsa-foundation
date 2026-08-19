using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Increment A0 gate (spec 123): proves the two test-harness prerequisites the fusion guardrail depends on are real,
/// BEFORE any fusion code exists — otherwise Increment C's guardrail would pass vacuously (research §8.4).
/// (P1) the harness can drive the SAME workflow coalesced (commits fold into a segment), and (P2) a
/// <see cref="ReplaySafeProbeActivity"/> node actually resolves a <see cref="SideEffectProfile.ReplaySafe"/> contract
/// while the unmarked <see cref="ProbeActivity"/> stays External.
/// </summary>
public sealed class ReplaySafeFusionHarnessPrerequisiteTests
{
    private const int HotLoopLength = 4;
    private static readonly string[] ActivityExecutionIds =
        Enumerable.Range(0, 16).Select(index => $"actexec-{index}").ToArray();

    [Fact]
    public async Task CoalescedHarnessPath_FoldsCheckpointsIntoASegment_AndStillCompletes()
    {
        var immediate = await DriveAsync(coalescing: false);
        var coalesced = await DriveAsync(coalescing: true);

        // Both runs reach the completed terminal (a silently-parked run would make any fold claim vacuous).
        Assert.True(immediate.Completed);
        Assert.True(coalesced.Completed);

        // The coalesced path genuinely folds the per-hop checkpoint storm into strictly fewer durable commits — this is
        // the burst the fusion pass engages inside. Without it, fusion never fires and the guardrail is meaningless.
        Assert.True(coalesced.CommitCount < immediate.CommitCount,
            $"Expected coalesced commits ({coalesced.CommitCount}) < immediate ({immediate.CommitCount}).");
    }

    [Fact]
    public void ReplaySafeProbeNode_ResolvesReplaySafeContract_WhileProbeStaysExternal()
    {
        var replaySafe = WorkflowExecutionHarness.NewReplaySafeProbeNode("node-1");
        var external = WorkflowExecutionHarness.NewProbeNode("node-2");

        Assert.NotNull(replaySafe.ActivityContract);
        Assert.Equal(SideEffectProfile.ReplaySafe, replaySafe.ActivityContract!.SideEffectProfile);

        Assert.NotNull(external.ActivityContract);
        Assert.Equal(SideEffectProfile.External, external.ActivityContract!.SideEffectProfile);
    }

    private static async Task<(bool Completed, int CommitCount)> DriveAsync(bool coalescing)
    {
        var builder = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartRuntimeFeature().ConfigureServices(services));
        if (coalescing)
            builder = builder.WithCoalescing();

        await using var harness = builder.Build(ActivityExecutionIds);

        var run = await harness.RunAsync(BuildReplaySafeStraightLineFlowchart());
        var commitCount = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits().Count;
        return (run.WorkflowState?.Status == WorkflowExecutionStatus.Completed, commitCount);
    }

    private static WorkflowExecutable BuildReplaySafeStraightLineFlowchart()
    {
        var leaves = Enumerable.Range(0, HotLoopLength)
            .Select(index => WorkflowExecutionHarness.NewReplaySafeProbeNode($"node-loop-{index}"))
            .ToArray();
        var connections = Enumerable.Range(0, HotLoopLength - 1)
            .Select(index => new FlowchartConnection(
                new FlowchartEndpoint(leaves[index].ExecutableNodeId),
                new FlowchartEndpoint(leaves[index + 1].ExecutableNodeId)))
            .ToArray();

        var root = new ExecutableNode(
            executableNodeId: "node-flowchart",
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "elsa.flowchart",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, leaves)],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(
                    new FlowchartStructure(connections: connections, startNodeId: leaves[0].ExecutableNodeId))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }
}
