using Elsa.Primitives.Models;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

public sealed class StepWorkflowStateRecoveryTests
{
    [Theory]
    [InlineData(LostUpdateMode.Default, false)]
    [InlineData(LostUpdateMode.Fault, false)]
    [InlineData(LostUpdateMode.Default, true)]
    public async Task Suspension_commits_and_fresh_resume_preserves_prior_state_on_fault(LostUpdateMode mode, bool bodyFault)
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness("actexec-parallel", "actexec-left", "actexec-right");
        var run = await StepVariableLostUpdateTests.RunAndResumeAsync(harness,
            bodyFault ? typeof(FaultingStateResumeWriter) : typeof(StateResumeWriter), StateResumeWriter.ResumeTargetKey, mode);
        var failed = mode == LostUpdateMode.Fault || bodyFault;
        Assert.Equal(failed ? ActivityExecutionStatus.Faulted : ActivityExecutionStatus.Completed, run.State("node-left").Status);
        Assert.Equal(failed ? "before" : "after", run.WorkflowState!.CorrelationId);
        var values = await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(harness.ExecutionId);
        Assert.Equal(failed ? "suspended" : "resumed", Assert.Single(values, value => value.ValueId == "output:phase").InlineValue!.Value.GetString());
        Assert.Equal(failed ? 1 : 10, VariableLifecycleTestGraph.ReadCount(run));
        var receipts = StepWorkflowStateEffects.ReadReceipts(run.State("node-left").Metadata);
        Assert.Equal(failed ? 1 : 2, receipts.Count);
        Assert.Equal("Suspension", receipts.First().Transition);
        Assert.Contains("outputs", receipts.First().Kinds);
        if (!failed) Assert.Equal("Completion", receipts.Last().Transition);
    }

    [Fact]
    public async Task State_only_capture_conflict_cannot_record_a_completion_that_would_omit_state_effects()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness("actexec-parallel", "actexec-left", "actexec-right");
        var right = StepVariableTestGraph.Leaf("node-right", typeof(StateOnlyCaptureWriter),
            new Dictionary<string, RuntimeInputBinding> { ["Seen"] = StepVariableTestGraph.ReadCount("Seen") },
            captureInto: VariableLifecycleTestGraph.CountKey, mode: LostUpdateMode.Fault);
        var run = await harness.RunAsync(StepVariableTestGraph.Parallel(
            VariableLifecycleTestGraph.Increment("node-left", typeof(IncrementActivity)), right));
        var failed = run.State("node-right");
        Assert.Equal(ActivityExecutionStatus.Faulted, failed.Status);
        Assert.Equal("VariableLostUpdate", failed.SubStatus);
        Assert.Null(failed.RecordedCompletion);
        Assert.Null(run.WorkflowState!.CorrelationId);
        var values = await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(harness.ExecutionId);
        Assert.DoesNotContain(values, value => value.ValueId == "output:state-only");
    }

    [Fact]
    public async Task State_only_resume_capture_conflict_cannot_accept_captures_without_state_effects()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness("actexec-parallel", "actexec-left", "actexec-right");
        var run = await StepVariableLostUpdateTests.RunAndResumeAsync(harness, typeof(StateOnlyResumeWriter), StateResumeWriter.ResumeTargetKey,
            LostUpdateMode.Fault, captureInto: VariableLifecycleTestGraph.CountKey);
        var failed = run.State("node-left");
        Assert.Equal(ActivityExecutionStatus.Faulted, failed.Status);
        Assert.Null(failed.RecordedCompletion);
        Assert.Equal("before", run.WorkflowState!.CorrelationId);
        var values = await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(harness.ExecutionId);
        Assert.Equal("suspended", Assert.Single(values, value => value.ValueId == "output:phase").InlineValue!.Value.GetString());
    }
}

[AccessesWorkflowState]
public class StateResumeWriter : StatefulActivity<Incremented, WaitState, WaitTrigger>
{
    public const string ResumeTargetKey = "resume-workflow-state";
    [ActivityInput] public int Seen { get; set; }
    [ActivityInput] public VariableReference<int> Count { get; set; } = default!;
    [ActivityInput] public VariableReference<int> Other { get; set; } = default!;

    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.WorkflowState.SetOutput("phase", JsonSerializer.SerializeToElement("suspended"));
        context.WorkflowState.CorrelationId = "before";
        return ValueTask.FromResult(Suspend(new WaitState("waiting"),
            [new ActivityTriggerRegistration<WaitTrigger>(ResumeTargetKey, StepVariableResume.StimulusType, StepVariableResume.StimulusHash)]));
    }

    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context)
    {
        Assert.Equal("before", context.WorkflowState.CorrelationId);
        Assert.True(context.WorkflowState.TryGetOutput("phase", out var previous));
        Assert.Equal("suspended", previous.GetString());
        context.Variables.Set(Other, 99);
        context.Variables.Set(Count, Seen + 10);
        context.WorkflowState.SetOutput("phase", JsonSerializer.SerializeToElement("resumed"));
        context.WorkflowState.CorrelationId = "after";
        return ValueTask.FromResult(Complete(new Incremented(Seen + 10)));
    }
}

public sealed class FaultingStateResumeWriter : StateResumeWriter
{
    protected override async ValueTask<ActivityTransition<Incremented, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context)
    {
        _ = await base.ResumeAsync(context);
        return Fault(new ActivityFault("test", "resume failed"));
    }
}

public sealed class StateOnlyResumeWriter : StateResumeWriter
{
    protected override ValueTask<ActivityTransition<Incremented, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context)
    {
        context.WorkflowState.SetOutput("phase", JsonSerializer.SerializeToElement("should-rollback"));
        context.WorkflowState.CorrelationId = "should-rollback";
        return ValueTask.FromResult(Complete(new Incremented(Seen + 10)));
    }
}

[AccessesWorkflowState]
public sealed class StateOnlyCaptureWriter : Activity<Incremented>
{
    [ActivityInput] public int Seen { get; set; }
    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.WorkflowState.CorrelationId = "should-rollback";
        context.WorkflowState.SetOutput("state-only", JsonSerializer.SerializeToElement(99));
        return ValueTask.FromResult(ActivityTransition.Complete(new Incremented(Seen + 1)));
    }
}
