using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// WU-3 (spec 109, ADR 0031 follow-up item (c)): the memory-is-never-correctness guardrail for the in-process-hop
/// payload short-circuit. The same deterministic multi-hop workflow is driven to completion twice over the same
/// in-memory durable substrate — once with the fast path ENABLED (the shipped default, so a live drain's checkpoint
/// committer publishes each continuation onto the drain-scoped carrier and the scheduler intent dispatcher takes it
/// instead of deserializing the durable payload) and once DISABLED (the durable deserialize path runs everywhere). The
/// two runs MUST commit byte-identical durable state. If the fast path ever let a cached object diverge from the
/// serialized form it would surface here as a byte difference. The direct dispatcher/committer/carrier mechanics
/// (engagement, absence fallback, crash-window redrive, single-use take, exec-id validation, the toggle) are covered by
/// RuntimeInProcessHopFastPathTests in Elsa.Workflows.Runtime.Tests.
/// </summary>
public sealed class RuntimeInProcessHopFastPathGuardrailTests
{
    private const int HotLoopLength = 4;
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ActivityExecutionIds =
        Enumerable.Range(0, 16).Select(index => $"actexec-{index}").ToArray();

    [Fact]
    public async Task FastPath_EnabledVersusDisabled_CommitsByteIdenticalDurableState()
    {
        var enabled = await DriveAsync(fastPathEnabled: true);
        var disabled = await DriveAsync(fastPathEnabled: false);

        // Both runs actually ran to completion (a silently-parked run would make "identical" vacuous).
        Assert.True(enabled.Completed);
        Assert.True(disabled.Completed);

        // The committed durable state — every checkpoint commit and the terminal activity/workflow snapshot — is
        // byte-for-byte identical whether the fast path skipped the JSON round-trip or not. Memory is a cache.
        Assert.Equal(disabled.CommitFingerprint, enabled.CommitFingerprint);
        Assert.Equal(disabled.StateFingerprint, enabled.StateFingerprint);
        Assert.NotEqual(string.Empty, enabled.CommitFingerprint);
    }

    // Self-check: the fingerprint is deterministic across two identical (disabled) runs, so the enabled-vs-disabled
    // equality above is a real convergence claim rather than an artifact of a non-deterministic capture.
    [Fact]
    public async Task DurableFingerprint_IsDeterministicAcrossRuns()
    {
        var first = await DriveAsync(fastPathEnabled: false);
        var second = await DriveAsync(fastPathEnabled: false);

        Assert.Equal(first.CommitFingerprint, second.CommitFingerprint);
        Assert.Equal(first.StateFingerprint, second.StateFingerprint);
    }

    private static async Task<RunFingerprint> DriveAsync(bool fastPathEnabled)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartRuntimeFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(new RuntimeInProcessHopFastPathOptions { Enabled = fastPathEnabled });
                // Pin the clock so the two runs stamp identical timestamps — the fast path never touches time stamping,
                // so this removes wall-clock noise without weakening the byte-identity claim.
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FakeTimeProvider(FixedNow)));
            })
            .Build(ActivityExecutionIds);

        // Sanity: the toggle took effect for this provider.
        Assert.Equal(fastPathEnabled, harness.Services.GetRequiredService<RuntimeInProcessHopFastPathOptions>().Enabled);

        var run = await harness.RunAsync(BuildStraightLineFlowchart());

        var commitStore = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var commitFingerprint = FingerprintCommits(commitStore.ListCommits());
        var stateFingerprint = FingerprintStates(run);

        return new RunFingerprint(
            Completed: run.WorkflowState?.Status == WorkflowExecutionStatus.Completed,
            CommitFingerprint: commitFingerprint,
            StateFingerprint: stateFingerprint);
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

    // Mask ownership-plane infrastructure ids that are freshly randomized per run and are unrelated to the fast path:
    // the drainer/outbox owner ids ("prefix:{Guid:N}"), and the per-acquisition execution lease/owner ids on the
    // operational liveness state (deliberately excluded from replay identity by the runtime). Both runs get the same
    // placeholder, so any genuine fast-path-induced divergence in the workflow's committed durable state still diffs.
    private static string Normalize(string fingerprint)
    {
        fingerprint = System.Text.RegularExpressions.Regex.Replace(fingerprint, "[0-9a-f]{32}", "<id>");
        fingerprint = System.Text.RegularExpressions.Regex.Replace(fingerprint, "\"(LeaseId|OwnerId)\":\"[^\"]*\"", "\"$1\":\"<id>\"");
        return fingerprint;
    }

    private static WorkflowExecutable BuildStraightLineFlowchart()
    {
        var leaves = Enumerable.Range(0, HotLoopLength)
            .Select(index => WorkflowExecutionHarness.NewProbeNode($"node-loop-{index}"))
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

    private sealed record RunFingerprint(bool Completed, string CommitFingerprint, string StateFingerprint);
}
