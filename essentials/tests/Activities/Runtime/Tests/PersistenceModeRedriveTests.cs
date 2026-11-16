using System.Collections.Concurrent;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Runtime.Tests.PersistenceModeTestRuns;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// A run keeps the persistence mode its workflow chose on every drain, also when its first pass ends before anything
/// was committed and the resumption sweep drives it again. The host default is a different mode in every case, and the
/// host keeps no completion record for an Ephemeral run that was never committed, so a drain that ran the host default
/// instead of the workflow's choice shows in what the run stores as well as in the mode the drain ran.
/// </summary>
public sealed class PersistenceModeRedriveTests
{
    private const string InterruptedNodeId = "node-interrupted";
    private static readonly string[] ActivityExecutionIds = Enumerable.Range(0, 32).Select(index => $"actexec-{index}").ToArray();

    private readonly ActivityRunLog _runs = new();
    private readonly ConcurrentQueue<PersistenceMode> _drainModes = new();

    public enum Interruption
    {
        HostStops,
        OwnershipLost
    }

    public static TheoryData<PersistenceMode, PersistenceMode, Interruption> ChosenModeOnAnotherHostDefault()
    {
        var data = new TheoryData<PersistenceMode, PersistenceMode, Interruption>();
        foreach (var chosen in Enum.GetValues<PersistenceMode>())
        foreach (var hostDefault in Enum.GetValues<PersistenceMode>().Where(mode => mode != chosen))
        foreach (var interruption in Enum.GetValues<Interruption>())
            data.Add(chosen, hostDefault, interruption);
        return data;
    }

    [Theory]
    [MemberData(nameof(ChosenModeOnAnotherHostDefault))]
    public async Task A_run_driven_again_before_its_first_commit_keeps_the_mode_its_workflow_chose(
        PersistenceMode chosen,
        PersistenceMode hostDefault,
        Interruption interruption)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => services.AddSingleton(_runs))
            .WithFeature(RecordDrainModes)
            .WithFeature(services =>
            {
                if (interruption == Interruption.HostStops)
                    InvokeHandlerInterruptions.StopHostOnFirstInvokeOf(services, InterruptedNodeId);
            })
            .WithPersistenceMode(hostDefault, recordCompletion: false)
            .WithWorkflowPersistenceMode(chosen)
            .Build(ActivityExecutionIds);

        // A single ReplaySafe activity: no mode commits anything before it runs, so the pass ends before the first commit.
        var activityType = interruption == Interruption.OwnershipLost
            ? typeof(LeaseStealingReplaySafeActivity)
            : typeof(CountingReplaySafeActivity);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewClrNode(InterruptedNodeId, activityType))));

        var commits = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        Assert.Empty(commits.ListCommits());
        Assert.NotEmpty(await QueuedWorkAsync(harness));

        await DeliverAsync(harness, WorkflowExecutionCommandKind.RunSchedulerWork, WorkflowExecutionActorActivationReason.Recovery);

        // Both passes ran the mode the workflow chose.
        Assert.Equal([chosen, chosen], _drainModes);
        var run = await harness.ReadRunAsync(harness.ExecutionId);
        if (chosen == PersistenceMode.Ephemeral)
        {
            Assert.Null(run.WorkflowState);
            Assert.Empty(commits.ListCommits());
        }
        else
        {
            Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
            Assert.Equal(chosen.ToString(), run.WorkflowState!.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            Assert.Single(commits.ListCommits(), commit => commit.Commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowCompleted);
        }

        Assert.Empty(await QueuedWorkAsync(harness));
    }

    // Records the mode each drain's session runs in, around the scope factory every drain begins its session from.
    private void RecordDrainModes(IServiceCollection services) =>
        services.AddScoped<IRuntimeCoalescingDrainScopeFactory>(serviceProvider => new ModeRecordingDrainScopeFactory(
            ActivatorUtilities.CreateInstance<RuntimeCoalescingDrainScopeFactory>(serviceProvider),
            _drainModes));

    private sealed class ModeRecordingDrainScopeFactory(
        IRuntimeCoalescingDrainScopeFactory inner,
        ConcurrentQueue<PersistenceMode> modes) : IRuntimeCoalescingDrainScopeFactory
    {
        public IRuntimeCoalescingDrainScope Begin(string workflowExecutionId, ResolvedPersistenceMode mode)
        {
            modes.Enqueue(mode.Mode);
            return inner.Begin(workflowExecutionId, mode);
        }
    }
}
