using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Runtime.Tests.PersistenceModeTestRuns;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class PersistenceModeFlushTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable, true, false)]
    [InlineData(PersistenceMode.Balanced, true, false)]
    [InlineData(PersistenceMode.Ephemeral, true, false)]
    [InlineData(PersistenceMode.Ephemeral, false, false)]
    [InlineData(PersistenceMode.Durable, true, true)]
    [InlineData(PersistenceMode.Balanced, true, true)]
    [InlineData(PersistenceMode.Ephemeral, true, true)]
    [InlineData(PersistenceMode.Ephemeral, false, true)]
    public async Task Workflow_state_uses_working_state_and_existing_mode_flush_rules(PersistenceMode mode, bool recordCompletion, bool previouslyPersisted)
    {
        await using var harness = NewHarness(mode, recordCompletion);
        var children = new List<ExecutableNode>();
        if (previouslyPersisted) children.Add(WorkflowExecutionHarness.NewProbeNode("persist-first"));
        children.Add(NewClrNode("write-state", typeof(ReplaySafeStateWriter)));
        children.Add(NewClrNode("read-state", typeof(ReplaySafeStateReader)));
        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSequence("sequence", children.ToArray())));
        Assert.Equal(2, _runs.Executions);
        var expectedRecord = previouslyPersisted || mode != PersistenceMode.Ephemeral || recordCompletion;
        var values = await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(harness.ExecutionId);
        if (expectedRecord)
        {
            Assert.Equal("new", run.WorkflowState!.CorrelationId);
            Assert.Equal("Ready", run.WorkflowState.SystemMetadata[RuntimeMetadataKeys.InstanceName]);
            Assert.Equal(3, Assert.Single(values, value => value.ValueId == "output:Count").InlineValue!.Value.GetInt32());
        }
        else
        {
            Assert.Null(run.WorkflowState);
            Assert.Empty(values);
        }
    }
}

[AccessesWorkflowState, ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class ReplaySafeStateWriter(ActivityRunLog runs, InMemoryRuntimeCheckpointCommitStore commits) : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        var before = commits.ListCommits().Count;
        for (var i = 1; i <= 3; i++) context.WorkflowState.SetOutput("Count", JsonSerializer.SerializeToElement(i));
        context.WorkflowState.CorrelationId = "new";
        context.WorkflowState.InstanceName = "Ready";
        Assert.Equal(before, commits.ListCommits().Count);
        runs.RecordExecution();
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}

[AccessesWorkflowState, ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class ReplaySafeStateReader(ActivityRunLog runs) : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        Assert.Equal("new", context.WorkflowState.CorrelationId);
        Assert.Equal("Ready", context.WorkflowState.InstanceName);
        Assert.True(context.WorkflowState.TryGetOutput("Count", out var count));
        Assert.Equal(3, count.GetInt32());
        runs.RecordExecution();
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
