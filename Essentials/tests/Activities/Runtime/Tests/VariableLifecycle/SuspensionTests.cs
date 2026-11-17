using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// US6 (spec 1221 T033, T035, FR-022): a suspended step keeps the input snapshot and read set it committed, resumes with
/// no refresh because time passed, and meets the commit rule on resume: a variable it only read never produces a lost
/// update, and an explicit write on a stale base warns by default and faults under <c>Fault</c>.
/// </summary>
public sealed class SuspensionTests
{
    private const string OtherKey = "var-other";
    private static readonly string[] Ids = ["actexec-parallel", "actexec-left", "actexec-right"];
    private static readonly string ResumeTargetId = $"resume:node-left:{WaitingIncrementActivity.ResumeTargetKey}";

    [Fact]
    public async Task A_resumed_write_on_a_base_that_moved_during_the_wait_applies_with_a_lost_update_warning()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var resumed = await RunAndResumeAsync(harness, LostUpdateMode.Default, captureKey: VariableLifecycleTestGraph.CountKey);

        // The waiting step read count = 0 before it suspended; the other branch wrote 1 meanwhile. On resume the step
        // still holds its committed snapshot (no refresh) and writes 0 + 1 on base 0: a lost update, applied with a warning.
        var left = resumed.State("node-left");
        Assert.Equal(0, left.InputSnapshot!.Values["Count"].InlineValue!.Value.GetInt32());
        Assert.Equal(0, Assert.Single(left.InputSnapshot.InputReadSet!.Entries).Version);
        var record = Assert.Single(left.VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, record.Outcome);
        Assert.Equal("node-right", record.OverwrittenWriter!.ExecutableNodeId);
        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(resumed));
    }

    [Fact]
    public async Task A_variable_the_resumed_step_only_read_produces_no_lost_update()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var resumed = await RunAndResumeAsync(harness, LostUpdateMode.Fault, captureKey: OtherKey);

        // It read count (which moved during the wait) but writes only other: no lost update, even under Fault.
        Assert.Equal(WorkflowExecutionStatus.Completed, resumed.WorkflowState!.Status);
        var record = Assert.Single(resumed.State("node-left").VariableWrites!);
        Assert.Equal(OtherKey, record.VariableKey);
        Assert.Equal(VariableCommitOutcome.Blind, record.Outcome);
        Assert.Equal(1, resumed.WorkflowState.RootVariableFrame!.Values[OtherKey].InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task Under_Fault_a_resumed_write_on_a_changed_base_faults()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var resumed = await RunAndResumeAsync(harness, LostUpdateMode.Fault, captureKey: VariableLifecycleTestGraph.CountKey);

        var left = resumed.State("node-left");
        Assert.Equal(ActivityExecutionStatus.Faulted, left.Status);
        Assert.Equal("VariableLostUpdate", left.SubStatus);
        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(resumed));
    }

    private static async Task<WorkflowExecutionRun> RunAndResumeAsync(WorkflowExecutionHarness harness, LostUpdateMode mode, string captureKey)
    {
        var executable = Executable(mode, captureKey);
        var suspended = await harness.RunAsync(executable);
        var waiting = suspended.State("node-left");
        Assert.Equal(ActivityExecutionStatus.Suspended, waiting.Status);
        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(suspended));

        return await harness.ResumeAsync(
            executable.Identity,
            bookmarkId: Assert.Single(waiting.BookmarkIds),
            activityExecutionId: waiting.InvocationId,
            executableNodeId: "node-left",
            resumeTargetId: ResumeTargetId,
            stimulusType: WaitingIncrementActivity.StimulusType,
            stimulusHash: WaitingIncrementActivity.StimulusHash,
            input: JsonSerializer.SerializeToElement(new WaitTrigger(Released: true)));
    }

    private static WorkflowExecutable Executable(LostUpdateMode mode, string captureKey) =>
        new(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: VariableLifecycleTestGraph.Parallel(
                VariableLifecycleTestGraph.Increment("node-left", typeof(WaitingIncrementActivity), mode, captureKey: captureKey),
                VariableLifecycleTestGraph.Increment("node-right", typeof(IncrementActivity))),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [ResumeTargetId] = new(
                    ResumeTargetId,
                    "node-left",
                    "ResumeAsync",
                    new Dictionary<string, string>(),
                    LocalResumeTargetId: WaitingIncrementActivity.ResumeTargetKey)
            },
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            workflowVariables:
            [
                VariableLifecycleTestGraph.CountDeclaration(),
                new RuntimeVariableDeclaration(OtherKey, "other", VariableLifecycleTestGraph.Int32Type, ValueProtectionPolicy.InstanceInline)
            ]);
}

/// <summary>An External leaf that reads <c>Count</c>, suspends, and completes with <c>Count + 1</c> when resumed.</summary>
public sealed class WaitingIncrementActivity : StatefulActivity<Incremented, WaitState, WaitTrigger>
{
    public const string ResumeTargetKey = "wait-increment";
    public const string StimulusType = "TestWaitIncrement";
    public const string StimulusHash = "wait:increment";

    [ActivityInput]
    public int Count { get; set; }

    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(Suspend(
            new WaitState("waiting"),
            [new ActivityTriggerRegistration<WaitTrigger>(ResumeTargetKey, StimulusType, StimulusHash)]));

    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context) =>
        ValueTask.FromResult(Complete(new Incremented(Count + 1), ActivityOutcomes.Done));
}
