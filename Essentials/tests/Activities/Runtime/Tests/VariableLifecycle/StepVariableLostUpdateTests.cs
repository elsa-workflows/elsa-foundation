using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1231 (S-7) User Story 3 (T011–T015): session writes meet the same lost-update rule as every other write (ADR
/// 0092 decision 1). Inline there is no gap; a stale base warns by default and faults under <c>Fault</c>; a blind write
/// is never a lost update; an input dependency is kept under a newer session read; and an invocation never conflicts
/// with its own writes.
/// </summary>
public sealed class StepVariableLostUpdateTests
{
    private static readonly string[] Ids = ["actexec-parallel", "actexec-left", "actexec-right"];

    [Fact]
    public async Task Two_parallel_inline_session_increments_total_two()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Parallel(
            StepVariableTestGraph.CountLeaf("node-left", typeof(SessionIncrementActivity)),
            StepVariableTestGraph.CountLeaf("node-right", typeof(SessionIncrementActivity))));

        run.AssertWorkflowCompleted();
        Assert.Equal(2, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Equal(VariableCommitOutcome.Current, Assert.Single(run.State("node-left").VariableWrites!).Outcome);
        var right = Assert.Single(run.State("node-right").VariableWrites!);
        Assert.Equal(VariableCommitOutcome.Current, right.Outcome);
        Assert.Equal((1L, 2L), (right.BaseVersion!.Value, right.NewVersion));
    }

    [Fact]
    public async Task A_resumed_write_built_on_an_input_read_that_moved_during_the_wait_is_a_lost_update_warning()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var resumed = await RunAndResumeAsync(harness, typeof(ResumeWriteFromInputActivity), ResumeWriteFromInputActivity.ResumeTargetKey, LostUpdateMode.Default);

        // The step read count = 0 as its input (version 0); the other branch wrote 1 meanwhile; the resumed session write of
        // Seen + 10 is built on version 0: a lost update that applies (last write wins), with a warning naming both writers
        // and keeping the overwritten 1 so it can be restored.
        var record = Assert.Single(resumed.State("node-left").VariableWrites!, write => write.VariableKey == VariableLifecycleTestGraph.CountKey);
        Assert.Equal(VariableWriteSource.SessionChange, record.Writer.Source);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, record.Outcome);
        Assert.Equal(0, record.BaseVersion);
        Assert.Equal(1, record.CurrentVersion);
        Assert.Equal(2, record.NewVersion);
        Assert.Equal("node-right", record.OverwrittenWriter!.ExecutableNodeId);
        Assert.Equal(1, record.OverwrittenValue!.InlineValue!.Value.GetInt32());
        Assert.Equal(10, VariableLifecycleTestGraph.ReadCount(resumed));
        Assert.Equal(99, StepVariableTestGraph.ReadInt(resumed, StepVariableTestGraph.OtherKey));
    }

    [Fact]
    public async Task Under_Fault_a_resumed_write_on_a_changed_base_faults_and_saves_nothing()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var resumed = await RunAndResumeAsync(harness, typeof(ResumeWriteFromInputActivity), ResumeWriteFromInputActivity.ResumeTargetKey, LostUpdateMode.Fault);

        var left = resumed.State("node-left");
        Assert.Equal(ActivityExecutionStatus.Faulted, left.Status);
        Assert.Equal("VariableLostUpdate", left.SubStatus);
        var refused = Assert.Single(left.VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateFault, refused.Outcome);
        Assert.Equal((0L, 1L), (refused.BaseVersion!.Value, refused.CurrentVersion));
        Assert.Equal("node-right", refused.OverwrittenWriter!.ExecutableNodeId);
        // The diagnostic names the variable, both versions and both writers (spec 1221 FR-013).
        var message = new Elsa.Activities.Runtime.Services.VariableLostUpdateException(refused).Message;
        Assert.Contains("'var-count'", message, StringComparison.Ordinal);
        Assert.Contains("read version 0", message, StringComparison.Ordinal);
        Assert.Contains("version 1", message, StringComparison.Ordinal);
        Assert.Contains("node-right", message, StringComparison.Ordinal);
        Assert.Contains("node-left", message, StringComparison.Ordinal);
        // Nothing it staged is saved: neither the refused count nor its other intent (other = 99).
        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(resumed));
        Assert.Equal(0, StepVariableTestGraph.ReadInt(resumed, StepVariableTestGraph.OtherKey));
    }

    [Fact]
    public async Task A_session_read_is_not_carried_across_a_suspension()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var resumed = await RunAndResumeAsync(harness, typeof(SessionReadThenResumeWriteActivity), SessionReadThenResumeWriteActivity.ResumeTargetKey,
            LostUpdateMode.Fault, readsInput: false);

        // ADR 0092 decision 11: the resumed step starts a fresh session. Its read before the wait gives no base, so its
        // write after the wait is blind (D-8), even under Fault.
        var left = resumed.State("node-left");
        Assert.Equal(ActivityExecutionStatus.Completed, left.Status);
        var record = Assert.Single(left.VariableWrites!);
        Assert.Equal(VariableCommitOutcome.Blind, record.Outcome);
        Assert.Equal(100, VariableLifecycleTestGraph.ReadCount(resumed));
        // The pre-suspension read is kept for inspection, with its own attempt.
        Assert.Single(left.SessionReads!);
    }

    [Fact]
    public async Task A_blind_session_write_is_never_a_lost_update()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Parallel(
            VariableLifecycleTestGraph.Increment("node-left", typeof(IncrementActivity), LostUpdateMode.Fault),
            StepVariableTestGraph.CountLeaf("node-right", typeof(BlindSessionWriteActivity), mode: LostUpdateMode.Fault)));

        run.AssertWorkflowCompleted();
        var record = Assert.Single(run.State("node-right").VariableWrites!);
        Assert.Equal(VariableCommitOutcome.Blind, record.Outcome);
        Assert.Equal(42, VariableLifecycleTestGraph.ReadCount(run));
    }

    [Fact]
    public async Task A_newer_session_read_does_not_hide_an_older_input_dependency()
    {
        var probe = new SessionReadProbe();
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(new RuntimeReplaySafeFusionOptions { Enabled = false });
                services.AddSingleton(probe);
            })
            .Build(Ids);
        var right = StepVariableTestGraph.Leaf("node-right", typeof(InputThenSessionReadActivity), new Dictionary<string, RuntimeInputBinding>
        {
            ["Seen"] = StepVariableTestGraph.ReadCount("Seen"),
            ["Count"] = StepVariableTestGraph.Reference("Count", "count", VariableLifecycleTestGraph.CountKey)
        }, captureInto: VariableLifecycleTestGraph.CountKey);

        var run = await harness.RunAsync(StepVariableTestGraph.Parallel(
            StepVariableTestGraph.CountLeaf("node-left", typeof(SetTenActivity)),
            right));

        // The right step read count = 0 as its input and, later, 10 in its session. Its capture (0 + 1) is computed from the
        // input, so its base is the input's version 0, not the session read's 1: a lost update, not a silent overwrite.
        Assert.Equal(10, probe.Value);
        var state = run.State("node-right");
        Assert.Equal(1, Assert.Single(state.SessionReads!).Entry.Version);
        var capture = Assert.Single(state.VariableWrites!);
        Assert.Equal(VariableWriteSource.Capture, capture.Writer.Source);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, capture.Outcome);
        Assert.Equal(0, capture.BaseVersion);
    }

    [Fact]
    public async Task A_session_write_on_suspension_and_a_capture_on_completion_do_not_conflict()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var resumed = await RunAndResumeAsync(harness, typeof(WriteOnSuspendThenCaptureActivity), WriteOnSuspendThenCaptureActivity.ResumeTargetKey,
            LostUpdateMode.Fault, readsInput: false, captureInto: VariableLifecycleTestGraph.CountKey, rightWritesCount: false);

        // The suspension committed 5 (version 1) and advanced the step's own base; the capture of 6 on completion is
        // judged against that own base: current, no self-conflict even under Fault.
        var left = resumed.State("node-left");
        Assert.Equal(ActivityExecutionStatus.Completed, left.Status);
        Assert.Equal(6, VariableLifecycleTestGraph.ReadCount(resumed));
        Assert.Collection(left.VariableWrites!,
            session =>
            {
                Assert.Equal(VariableWriteSource.SessionChange, session.Writer.Source);
                Assert.Equal(1, session.NewVersion);
            },
            capture =>
            {
                Assert.Equal(VariableWriteSource.Capture, capture.Writer.Source);
                Assert.Equal(VariableCommitOutcome.Current, capture.Outcome);
                Assert.Equal((1L, 2L), (capture.BaseVersion!.Value, capture.NewVersion));
            });
    }

    [Fact]
    public async Task In_one_commit_the_session_write_applies_first_and_the_capture_wins()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(["actexec-root"]);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(
            StepVariableTestGraph.CountLeaf("node-root", typeof(WriteAndCaptureActivity), captureInto: VariableLifecycleTestGraph.CountKey, mode: LostUpdateMode.Fault)));

        run.AssertWorkflowCompleted();
        Assert.Equal(7, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Collection(run.State("node-root").VariableWrites!,
            session => Assert.Equal((VariableWriteSource.SessionChange, 1L), (session.Writer.Source, session.NewVersion)),
            capture => Assert.Equal((VariableWriteSource.Capture, VariableCommitOutcome.Current, 2L), (capture.Writer.Source, capture.Outcome, capture.NewVersion)));
    }

    private static async Task<WorkflowExecutionRun> RunAndResumeAsync(
        WorkflowExecutionHarness harness,
        Type waitingActivity,
        string resumeTargetKey,
        LostUpdateMode mode,
        bool readsInput = true,
        string? captureInto = null,
        bool rightWritesCount = true)
    {
        var resumeTargetId = $"resume:node-left:{resumeTargetKey}";
        var bindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["Count"] = StepVariableTestGraph.Reference("Count", "count", VariableLifecycleTestGraph.CountKey)
        };
        if (readsInput)
        {
            bindings["Seen"] = StepVariableTestGraph.ReadCount("Seen");
            bindings["Other"] = StepVariableTestGraph.Reference("Other", "other", StepVariableTestGraph.OtherKey);
        }
        var left = StepVariableTestGraph.Leaf("node-left", waitingActivity, bindings, captureInto, mode);
        var right = rightWritesCount
            ? VariableLifecycleTestGraph.Increment("node-right", typeof(IncrementActivity))
            : VariableLifecycleTestGraph.Increment("node-right", typeof(IncrementActivity), captureKey: StepVariableTestGraph.OtherKey);
        var executable = new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: VariableLifecycleTestGraph.Parallel(left, right),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [resumeTargetId] = new(resumeTargetId, "node-left", "ResumeAsync", new Dictionary<string, string>(), LocalResumeTargetId: resumeTargetKey)
            },
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            workflowVariables: [VariableLifecycleTestGraph.CountDeclaration(), StepVariableTestGraph.OtherDeclaration()]);

        var suspended = await harness.RunAsync(executable);
        var waiting = suspended.State("node-left");
        Assert.Equal(ActivityExecutionStatus.Suspended, waiting.Status);

        return await harness.ResumeAsync(
            executable.Identity,
            bookmarkId: Assert.Single(waiting.BookmarkIds),
            activityExecutionId: waiting.InvocationId,
            executableNodeId: "node-left",
            resumeTargetId: resumeTargetId,
            stimulusType: StepVariableResume.StimulusType,
            stimulusHash: StepVariableResume.StimulusHash,
            input: JsonSerializer.SerializeToElement(new WaitTrigger(Released: true)));
    }
}
