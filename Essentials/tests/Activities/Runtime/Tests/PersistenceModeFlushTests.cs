using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// What each persistence mode commits, observed on the durable store the harness wraps: which checkpoints reach it and
/// how often. The runs use real activities through the production runtime over in-memory stores.
/// </summary>
public sealed class PersistenceModeFlushTests
{
    private const int StraightLineLength = 7;
    private static readonly string[] ActivityExecutionIds = Enumerable.Range(0, 32).Select(index => $"actexec-{index}").ToArray();

    [Fact]
    public async Task Balanced_counts_MaxActivitiesPerCommit_in_activities_not_checkpoints()
    {
        var uncapped = await RunStraightLineAsync(maxActivitiesPerCommit: 50);
        Assert.Equal(1, uncapped.Commits);

        // Each activity produces several checkpoints, so a cap counted in checkpoints would commit many times here. A cap
        // equal to the number of activities the run touches fits the whole run in one commit; one fewer forces exactly one
        // intermediate commit when the last new activity arrives.
        var atActivityCount = await RunStraightLineAsync(maxActivitiesPerCommit: uncapped.Activities);
        var belowActivityCount = await RunStraightLineAsync(maxActivitiesPerCommit: uncapped.Activities - 1);

        Assert.Equal(1, atActivityCount.Commits);
        Assert.Equal(2, belowActivityCount.Commits);
        Assert.Equal(uncapped.Snapshot, belowActivityCount.Snapshot);
    }

    [Fact]
    public async Task Balanced_with_the_smallest_cap_still_completes()
    {
        // With a cap of one, a parent's checkpoint can bring two activities into an empty segment at once. That segment
        // must take the checkpoint rather than fold an empty buffer, or the drain loses the in-flight work and the run
        // stops in Running with nothing queued.
        var uncapped = await RunStraightLineAsync(maxActivitiesPerCommit: 50);
        var smallest = await RunStraightLineAsync(maxActivitiesPerCommit: 1);

        Assert.Equal(uncapped.Snapshot, smallest.Snapshot);
        Assert.True(smallest.Commits > uncapped.Commits);
    }

    private static async Task<(int Activities, int Commits, IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)> Snapshot)> RunStraightLineAsync(
        int maxActivitiesPerCommit)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .WithPersistenceMode(PersistenceMode.Balanced, maxActivitiesPerCommit)
            .Build(ActivityExecutionIds);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSequence(
            "node-sequence",
            Enumerable.Range(0, StraightLineLength)
                .Select(index => WorkflowExecutionHarness.NewReplaySafeProbeNode($"node-probe-{index}"))
                .ToArray())));

        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        var commits = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits().Count;
        var snapshot = run.ActivityStates
            .Select(state => (state.Execution.ExecutableNodeId, state.Status))
            .OrderBy(entry => entry.ExecutableNodeId, StringComparer.Ordinal)
            .ToArray();
        return (run.ActivityStates.Count, commits, snapshot);
    }

    private static ExecutableNode NewSequence(string nodeId, IReadOnlyList<ExecutableNode> activities) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, activities)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = activities.Select(activity => activity.ExecutableNodeId).ToArray() })));
}
