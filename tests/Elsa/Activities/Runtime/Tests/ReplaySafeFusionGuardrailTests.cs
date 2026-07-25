using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 123 guardrail 1 (ADR 0047): ReplaySafe hop fusion is a pure dispatch-locality optimization — a run with fusion
/// ENABLED commits byte-identical durable state to a run with it DISABLED, for every shape, and never changes
/// <c>External</c>/unmarked behavior. Every shape is driven twice over the same in-memory durable substrate under the
/// coalescing policy (fusion only engages inside a live burst): once fusion-ON (the shipped default) and once
/// fusion-OFF (<c>RuntimeReplaySafeFusionOptions.Enabled = false</c>). The committed checkpoint/state documents MUST be
/// byte-for-byte identical. A determinism self-check (two OFF runs) proves the fingerprint is a real convergence claim,
/// and the fusion counter proves the ON run actually engaged fusion — a guardrail that passes with zero fused spans is
/// a failed guardrail.
/// </summary>
public sealed class ReplaySafeFusionGuardrailTests
{
    private const int HotLoopLength = 5;
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ActivityExecutionIds =
        Enumerable.Range(0, 24).Select(index => $"actexec-{index}").ToArray();

    // Shape (a): a straight-line hot loop of ReplaySafe leaves. Fusion must engage on every leaf and commit
    // byte-identical durable state.
    [Fact]
    public async Task StraightLineReplaySafeHotLoop_EnabledVersusDisabled_IsByteIdentical_AndFusionEngages()
    {
        var enabled = await DriveAsync(BuildReplaySafeHotLoop, fusionEnabled: true);
        var disabled = await DriveAsync(BuildReplaySafeHotLoop, fusionEnabled: false);

        Assert.True(enabled.Completed);
        Assert.True(disabled.Completed);
        Assert.Equal(disabled.CommitFingerprint, enabled.CommitFingerprint);
        Assert.Equal(disabled.StateFingerprint, enabled.StateFingerprint);
        Assert.NotEqual(string.Empty, enabled.CommitFingerprint);

        // Non-vacuous: fusion actually engaged in the ON run (one fused span per leaf) and did not in the OFF run,
        // and the ON run paid materially fewer scheduler dispatches (the start+invoke hops were fused away).
        Assert.True(enabled.FusedSpans >= HotLoopLength,
            $"Expected >= {HotLoopLength} fused spans in the ON run, saw {enabled.FusedSpans}.");
        Assert.Equal(0, disabled.FusedSpans);
        Assert.True(enabled.Dispatches < disabled.Dispatches,
            $"Expected ON dispatches ({enabled.Dispatches}) < OFF dispatches ({disabled.Dispatches}).");

        // D2 engaged too: the completion cascade between fused spans was pumped inline, not drained per item.
        Assert.True(enabled.InlineCascadeDispatches > 0,
            $"Expected the D2 pump to dispatch cascade items inline in the ON run, saw {enabled.InlineCascadeDispatches}.");
        Assert.Equal(0, disabled.InlineCascadeDispatches);
    }

    // D1-only variant: the completion-cascade dial OFF (D1 fused schedule→start→invoke, discrete cascade) must also be
    // byte-identical to no-fusion — the benchmark's middle column is a correctness point, not just a measurement.
    [Fact]
    public async Task StraightLineReplaySafeHotLoop_D1OnlyVersusDisabled_IsByteIdentical()
    {
        var d1Only = await DriveAsync(BuildReplaySafeHotLoop, new RuntimeReplaySafeFusionOptions { Enabled = true, FuseCompletionCascade = false });
        var disabled = await DriveAsync(BuildReplaySafeHotLoop, fusionEnabled: false);

        Assert.True(d1Only.Completed);
        Assert.Equal(disabled.CommitFingerprint, d1Only.CommitFingerprint);
        Assert.Equal(disabled.StateFingerprint, d1Only.StateFingerprint);
        Assert.True(d1Only.FusedSpans >= HotLoopLength);
        Assert.Equal(0, d1Only.InlineCascadeDispatches);
    }

    // Shape (b): a multi-outcome branch — one ReplaySafe node forking to two single-predecessor ReplaySafe tails.
    // Every successor edge is provably single-predecessor, so the D2 pump carries both branches inline (FIFO, exactly
    // the discrete order) and no edge is treated as a join.
    [Fact]
    public async Task MultiOutcomeBranch_EnabledVersusDisabled_IsByteIdentical_AndCascadePumpsInline()
    {
        var enabled = await DriveAsync(BuildReplaySafeBranchShape, fusionEnabled: true);
        var disabled = await DriveAsync(BuildReplaySafeBranchShape, fusionEnabled: false);

        Assert.True(enabled.Completed);
        Assert.True(disabled.Completed);
        Assert.Equal(disabled.CommitFingerprint, enabled.CommitFingerprint);
        Assert.Equal(disabled.StateFingerprint, enabled.StateFingerprint);
        Assert.NotEqual(string.Empty, enabled.CommitFingerprint);

        // Non-vacuous: the fork node, both branch tails, and the composite all fused, and the cascade pumped inline;
        // no branch successor was mistaken for a join.
        Assert.True(enabled.FusedSpans >= 4,
            $"Expected the fork, both tails and the composite to fuse, saw {enabled.FusedSpans} fused spans.");
        Assert.True(enabled.InlineCascadeDispatches > 0,
            $"Expected the D2 pump to dispatch cascade items inline, saw {enabled.InlineCascadeDispatches}.");
        Assert.Equal(0, enabled.CascadeJoinFallbacks);
        Assert.Equal(0, disabled.FusedSpans);
        Assert.Equal(0, disabled.InlineCascadeDispatches);
        Assert.True(enabled.Dispatches < disabled.Dispatches,
            $"Expected ON dispatches ({enabled.Dispatches}) < OFF dispatches ({disabled.Dispatches}).");
    }

    // Shape (c): a fan-in join (diamond). The join node's ScheduleActivity has two inbound edges, so the D2 pump MUST
    // hand it back to the discrete cascade (ADR 0047 resolution #1) — proven via the join-fallback counter — and the
    // committed durable state must still be byte-identical.
    [Fact]
    public async Task FanInJoin_EnabledVersusDisabled_IsByteIdentical_AndJoinEdgeFallsBackToDiscreteCascade()
    {
        var enabled = await DriveAsync(BuildReplaySafeDiamondShape, fusionEnabled: true);
        var disabled = await DriveAsync(BuildReplaySafeDiamondShape, fusionEnabled: false);

        Assert.True(enabled.Completed);
        Assert.True(disabled.Completed);
        Assert.Equal(disabled.CommitFingerprint, enabled.CommitFingerprint);
        Assert.Equal(disabled.StateFingerprint, enabled.StateFingerprint);
        Assert.NotEqual(string.Empty, enabled.CommitFingerprint);

        // Non-vacuous: fusion + the inline cascade engaged on the branches, AND the join edge demonstrably fell back
        // to the discrete drain loop instead of being dispatched inline.
        Assert.True(enabled.FusedSpans >= 3,
            $"Expected the fork and both branches to fuse, saw {enabled.FusedSpans} fused spans.");
        Assert.True(enabled.InlineCascadeDispatches > 0,
            $"Expected the D2 pump to dispatch cascade items inline, saw {enabled.InlineCascadeDispatches}.");
        Assert.True(enabled.CascadeJoinFallbacks >= 1,
            $"Expected the join edge to fall back to the discrete cascade, saw {enabled.CascadeJoinFallbacks} join fallbacks.");
        Assert.Equal(0, disabled.FusedSpans);
        Assert.Equal(0, disabled.InlineCascadeDispatches);
        Assert.Equal(0, disabled.CascadeJoinFallbacks);
    }

    // Shape (d): a ReplaySafe leaf whose body suspends on a bookmark — the fused span exits mid-way at the suspension
    // commit. Byte-identical because fusion dispatches the unchanged invoke handler inline.
    [Fact]
    public async Task SuspendingReplaySafeLeaf_EnabledVersusDisabled_IsByteIdentical_AndFusionEngages()
    {
        var enabled = await DriveAsync(BuildReplaySafeSuspendShape, fusionEnabled: true);
        var disabled = await DriveAsync(BuildReplaySafeSuspendShape, fusionEnabled: false);

        // The run suspends (not completed); the committed state up to the suspension boundary must be identical.
        Assert.Equal(disabled.CommitFingerprint, enabled.CommitFingerprint);
        Assert.Equal(disabled.StateFingerprint, enabled.StateFingerprint);
        Assert.NotEqual(string.Empty, enabled.CommitFingerprint);

        Assert.True(enabled.FusedSpans >= 1,
            $"Expected the ReplaySafe probe+waiting leaves to fuse in the ON run, saw {enabled.FusedSpans}.");
        Assert.Equal(0, disabled.FusedSpans);
    }

    // Shape (e): an External (unmarked) hot loop. Fusion must NOT engage; the two runs are trivially identical.
    [Fact]
    public async Task ExternalHotLoop_EnabledVersusDisabled_IsByteIdentical_AndNeverFuses()
    {
        var enabled = await DriveAsync(BuildExternalHotLoop, fusionEnabled: true);
        var disabled = await DriveAsync(BuildExternalHotLoop, fusionEnabled: false);

        Assert.True(enabled.Completed);
        Assert.True(disabled.Completed);
        Assert.Equal(disabled.CommitFingerprint, enabled.CommitFingerprint);
        Assert.Equal(disabled.StateFingerprint, enabled.StateFingerprint);

        // External is the fail-safe default: none of the External leaves fuse (at most the ReplaySafe Flowchart
        // composite that hosts them does — its child-scheduling continuation still flows via the overlay). If any
        // External leaf had fused, the count would reach the loop length; it must stay strictly below it.
        Assert.True(enabled.FusedSpans < HotLoopLength,
            $"External leaves must not fuse, but saw {enabled.FusedSpans} fused spans for {HotLoopLength} leaves.");
        Assert.Equal(0, disabled.FusedSpans);
    }

    // Self-check: the fingerprint is deterministic across two identical (fusion-OFF) runs, so the enabled-vs-disabled
    // equality above is a real convergence claim rather than an artifact of a non-deterministic capture.
    [Fact]
    public async Task DurableFingerprint_IsDeterministicAcrossRuns()
    {
        var first = await DriveAsync(BuildReplaySafeHotLoop, fusionEnabled: false);
        var second = await DriveAsync(BuildReplaySafeHotLoop, fusionEnabled: false);

        Assert.Equal(first.CommitFingerprint, second.CommitFingerprint);
        Assert.Equal(first.StateFingerprint, second.StateFingerprint);
    }

    private static Task<RunFingerprint> DriveAsync(Func<WorkflowExecutable> buildExecutable, bool fusionEnabled) =>
        DriveAsync(buildExecutable, new RuntimeReplaySafeFusionOptions { Enabled = fusionEnabled });

    private static async Task<RunFingerprint> DriveAsync(Func<WorkflowExecutable> buildExecutable, RuntimeReplaySafeFusionOptions fusionOptions)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithCoalescing()
            .ConfigureServices(services =>
            {
                // AddSingleton (not TryAdd) supersedes the runtime's default registration for GetService resolution —
                // the same A/B lever the spec-109 fast-path guardrail uses.
                services.AddSingleton(fusionOptions);
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(FixedNow)));
            })
            .Build(ActivityExecutionIds);

        Assert.Equal(fusionOptions.Enabled, harness.Services.GetRequiredService<RuntimeReplaySafeFusionOptions>().Enabled);

        var run = await harness.RunAsync(buildExecutable());

        var commitStore = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var diagnostics = harness.Services.GetRequiredService<RuntimeSchedulerDispatchDiagnostics>();

        return new RunFingerprint(
            Completed: run.WorkflowState?.Status == WorkflowExecutionStatus.Completed,
            CommitFingerprint: FingerprintCommits(commitStore.ListCommits()),
            StateFingerprint: FingerprintStates(run),
            FusedSpans: diagnostics.FusedSpans,
            Dispatches: diagnostics.Dispatches,
            InlineCascadeDispatches: diagnostics.InlineCascadeDispatches,
            CascadeJoinFallbacks: diagnostics.CascadeJoinFallbacks);
    }

    private static string FingerprintCommits(IReadOnlyCollection<RuntimeCheckpointCommitRecord> commits) =>
        Normalize(string.Join(
            "\n",
            commits
                .OrderBy(record => record.Commit.CommitId, StringComparer.Ordinal)
                .Select(record => JsonSerializer.Serialize(record.Commit))));

    private static string FingerprintStates(WorkflowExecutionRun run)
    {
        var activities = run.ActivityStates
            .OrderBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .Select(state => JsonSerializer.Serialize(state));
        return Normalize(string.Join("\n", activities) + "\n---\n" + JsonSerializer.Serialize(run.WorkflowState));
    }

    // Mask ownership-plane infrastructure ids freshly randomized per run and unrelated to fusion (drainer/outbox owner
    // ids "prefix:{Guid:N}", per-acquisition lease/owner ids). Both runs get the same placeholder, so any genuine
    // fusion-induced divergence in the workflow's committed durable state still diffs.
    private static string Normalize(string fingerprint)
    {
        fingerprint = System.Text.RegularExpressions.Regex.Replace(fingerprint, "[0-9a-f]{32}", "<id>");
        fingerprint = System.Text.RegularExpressions.Regex.Replace(fingerprint, "\"(LeaseId|OwnerId)\":\"[^\"]*\"", "\"$1\":\"<id>\"");
        return fingerprint;
    }

    private static WorkflowExecutable BuildReplaySafeHotLoop() =>
        BuildStraightLine(Enumerable.Range(0, HotLoopLength)
            .Select(index => WorkflowExecutionHarness.NewReplaySafeProbeNode($"node-loop-{index}"))
            .ToArray());

    private static WorkflowExecutable BuildExternalHotLoop() =>
        BuildStraightLine(Enumerable.Range(0, HotLoopLength)
            .Select(index => WorkflowExecutionHarness.NewProbeNode($"node-loop-{index}"))
            .ToArray());

    private static WorkflowExecutable BuildReplaySafeSuspendShape()
    {
        var probe = WorkflowExecutionHarness.NewReplaySafeProbeNode("node-probe");
        var waiting = NewReplaySafeWaitingNode("node-wait");
        return BuildStraightLine([probe, waiting], waitingNodeId: "node-wait");
    }

    // Shape (b): node-fork branches to two independent single-predecessor tails (no reconvergence).
    private static WorkflowExecutable BuildReplaySafeBranchShape()
    {
        var fork = WorkflowExecutionHarness.NewReplaySafeProbeNode("node-fork");
        var left = WorkflowExecutionHarness.NewReplaySafeProbeNode("node-left");
        var right = WorkflowExecutionHarness.NewReplaySafeProbeNode("node-right");
        return BuildFlowchart(
            [fork, left, right],
            [
                new FlowchartConnection(new FlowchartEndpoint("node-fork"), new FlowchartEndpoint("node-left")),
                new FlowchartConnection(new FlowchartEndpoint("node-fork"), new FlowchartEndpoint("node-right"))
            ],
            startNodeId: "node-fork");
    }

    // Shape (c): diamond — node-fork branches to two tails that reconverge on node-join (two inbound edges).
    private static WorkflowExecutable BuildReplaySafeDiamondShape()
    {
        var fork = WorkflowExecutionHarness.NewReplaySafeProbeNode("node-fork");
        var left = WorkflowExecutionHarness.NewReplaySafeProbeNode("node-left");
        var right = WorkflowExecutionHarness.NewReplaySafeProbeNode("node-right");
        var join = WorkflowExecutionHarness.NewReplaySafeProbeNode("node-join");
        return BuildFlowchart(
            [fork, left, right, join],
            [
                new FlowchartConnection(new FlowchartEndpoint("node-fork"), new FlowchartEndpoint("node-left")),
                new FlowchartConnection(new FlowchartEndpoint("node-fork"), new FlowchartEndpoint("node-right")),
                new FlowchartConnection(new FlowchartEndpoint("node-left"), new FlowchartEndpoint("node-join")),
                new FlowchartConnection(new FlowchartEndpoint("node-right"), new FlowchartEndpoint("node-join"))
            ],
            startNodeId: "node-fork");
    }

    private static WorkflowExecutable BuildStraightLine(ExecutableNode[] leaves, string? waitingNodeId = null)
    {
        var connections = Enumerable.Range(0, leaves.Length - 1)
            .Select(index => new FlowchartConnection(
                new FlowchartEndpoint(leaves[index].ExecutableNodeId),
                new FlowchartEndpoint(leaves[index + 1].ExecutableNodeId)))
            .ToArray();

        return BuildFlowchart(leaves, connections, leaves[0].ExecutableNodeId, waitingNodeId);
    }

    private static WorkflowExecutable BuildFlowchart(
        ExecutableNode[] leaves,
        FlowchartConnection[] connections,
        string startNodeId,
        string? waitingNodeId = null)
    {
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
                    new FlowchartStructure(connections: connections, startNodeId: startNodeId))));

        var resumeTargets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal);
        if (waitingNodeId is not null)
        {
            var resumeTargetId = $"resume:{waitingNodeId}:{ReplaySafeWaitingActivity.ResumeTargetKey}";
            resumeTargets[resumeTargetId] = new WorkflowExecutableResumeTarget(
                resumeTargetId,
                waitingNodeId,
                "ResumeAsync",
                new Dictionary<string, string>(),
                LocalResumeTargetId: ReplaySafeWaitingActivity.ResumeTargetKey);
        }

        return new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: root,
            resumeTargets: resumeTargets,
            createdAt: FixedNow,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private static ExecutableNode NewReplaySafeWaitingNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(ReplaySafeWaitingActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/waiting-replay-safe",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "waiting" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

    private sealed record RunFingerprint(
        bool Completed,
        string CommitFingerprint,
        string StateFingerprint,
        long FusedSpans,
        long Dispatches,
        long InlineCascadeDispatches,
        long CascadeJoinFallbacks);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
