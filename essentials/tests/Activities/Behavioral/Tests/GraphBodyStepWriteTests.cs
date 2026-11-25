using System.Text.Json;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Behavioral.GraphBodyScopeTests;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// Spec 1241 (T007): a step inside a reusable activity's body writes its body's own variables through its variable
/// session, as a step in a workflow writes workflow variables. The write lands in the body's variable row, is judged by
/// the commit rule, commits with the step and never reaches the caller's variables.
/// </summary>
public sealed class GraphBodyStepWriteTests
{
    private static readonly JsonElement Int32Type = JsonSerializer.SerializeToElement(new { alias = "Int32" });

    [Fact]
    public async Task A_body_script_changes_its_variable_and_the_next_step_and_the_output_read_it()
    {
        // US1 AS1, SC-005: the script returns 90, the next body step reads 90, the activity's output is 90, and the
        // caller's variable of the same name keeps its value.
        await using var harness = NewHarness();
        var body = GraphBoundaryCompleteTests.Sequence(
            "node-body",
            Script("node-js", "variables.Total = variables.Total * 0.9; return variables.Total;"),
            Echo("node-read-total", Variable("total", BoundaryNodeId)));
        var boundary = Boundary(
            body,
            variables: [TotalVariable(100)],
            outputs: [new GraphActivityOutputDescriptor("sum", ObjectType, WellKnownRuntimeDurableValueStorageDrivers.Json, "Sum")],
            outputMappings: [new GraphActivityOutputMappingDescriptor("sum", new GraphActivityExpression("JavaScript", JsonSerializer.SerializeToElement("getVariable('Total')")))]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(boundary, CallerTotal()));

        run.AssertWorkflowCompleted();
        Assert.Equal(90, run.AssertCompleted("node-js").Completion!.Result.InlineValue!.Value.GetProperty("value").GetInt32());
        Assert.Equal(90, ReadInput(run, "node-read-total"));
        var output = Assert.Single(await RowsAsync(harness, run), value => value.DurableValueId.EndsWith(":output:3:sum", StringComparison.Ordinal));
        Assert.Equal(90, output.InlineValue!.Value.GetInt32());
        Assert.Equal(0, run.WorkflowState!.RootVariableFrame!.Values["total"].InlineValue!.Value.GetInt32());

        // US2 AS1: the row's version advances, and the write history records it as a session change by the step.
        var write = Assert.Single(run.State("node-js").VariableWrites!);
        Assert.Equal(VariableWriteSource.SessionChange, write.Writer.Source);
        Assert.Equal("node-js", write.Writer.ExecutableNodeId);
        Assert.Equal(BoundaryNodeId, write.ScopeId);
        Assert.Equal("total", write.VariableKey);
        Assert.True(write.NewVersion > write.CurrentVersion);
        var row = await TotalRowAsync(harness, run);
        Assert.Equal(write.NewVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Metadata[RuntimeMetadataKeys.RowVariableVersion]);
        Assert.Contains("node-js", row.Metadata[RuntimeMetadataKeys.RowVariableWriter], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_step_writes_its_variable_through_a_writable_reference()
    {
        // US1 AS2.
        await using var harness = NewHarness();
        var body = GraphBoundaryCompleteTests.Sequence(
            "node-body",
            TotalLeaf("node-set", typeof(SetTotalToFiveActivity)),
            Echo("node-read-total", Variable("total", BoundaryNodeId)));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Boundary(body, variables: [TotalVariable(100)]), CallerTotal()));

        run.AssertWorkflowCompleted();
        Assert.Equal(5, ReadInput(run, "node-read-total"));
        Assert.Equal(VariableWriteSource.SessionChange, Assert.Single(run.State("node-set").VariableWrites!).Writer.Source);
        Assert.Equal(0, run.WorkflowState!.RootVariableFrame!.Values["total"].InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task A_marked_body_step_writes_its_variable_by_name()
    {
        // US1 AS3.
        await using var harness = NewHarness();
        var body = GraphBoundaryCompleteTests.Sequence(
            "node-body",
            Leaf("node-set", typeof(IncrementTotalByNameActivity), new Dictionary<string, RuntimeInputBinding>()),
            Echo("node-read-total", Variable("total", BoundaryNodeId)));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Boundary(body, variables: [TotalVariable(100)]), CallerTotal()));

        run.AssertWorkflowCompleted();
        Assert.Equal(101, ReadInput(run, "node-read-total"));
        Assert.Equal(0, run.WorkflowState!.RootVariableFrame!.Values["total"].InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task Two_activations_change_their_own_variable()
    {
        // US1 AS5: each activation's body has its own row.
        await using var harness = NewHarness();
        var first = Boundary(
            GraphBoundaryCompleteTests.Sequence("node-body-1", TotalLeaf("node-inc-1", typeof(IncrementTotalActivity), scopeId: "node-boundary-1"),
                Echo("node-read-1", Variable("total", "node-boundary-1"))),
            variables: [TotalVariable(10)], nodeId: "node-boundary-1");
        var second = Boundary(
            GraphBoundaryCompleteTests.Sequence("node-body-2", TotalLeaf("node-inc-2", typeof(IncrementTotalActivity), scopeId: "node-boundary-2"),
                Echo("node-read-2", Variable("total", "node-boundary-2"))),
            variables: [TotalVariable(10)], nodeId: "node-boundary-2");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(GraphBoundaryCompleteTests.Sequence("node-root", first, second), CallerTotal()));

        run.AssertWorkflowCompleted();
        Assert.Equal(11, ReadInput(run, "node-read-1"));
        Assert.Equal(11, ReadInput(run, "node-read-2"));
    }

    [Fact]
    public async Task A_nested_body_writes_its_own_variable_not_the_outer_bodys()
    {
        // Edge case: a step reaches its nearest body only.
        await using var harness = NewHarness();
        var inner = Boundary(
            TotalLeaf("node-set", typeof(SetTotalToFiveActivity), scopeId: "node-inner"),
            variables: [TotalVariable(1)], nodeId: "node-inner");
        var outer = Boundary(
            GraphBoundaryCompleteTests.Sequence("node-body", inner, Echo("node-read-outer", Variable("total", BoundaryNodeId))),
            variables: [TotalVariable(100)]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(outer));

        run.AssertWorkflowCompleted();
        Assert.Equal(100, ReadInput(run, "node-read-outer"));
        Assert.Equal("node-inner", Assert.Single(run.State("node-set").VariableWrites!).ScopeId);
    }

    [Fact]
    public async Task A_step_that_writes_and_captures_one_variable_commits_both_and_the_capture_wins()
    {
        // US2 AS3, FR-006, FR-007: the session write goes first and advances the step's base, so its own capture is
        // judged against the row it left and does not conflict with it.
        await using var harness = NewHarness();
        var leaf = TotalLeaf("node-write-capture", typeof(WriteFiveReturnSevenActivity));
        var body = GraphBoundaryCompleteTests.Sequence(
            "node-body",
            CaptureInto(leaf, "total", ProjectionKey(leaf, nameof(TotalResult.Value))),
            Echo("node-read-total", Variable("total", BoundaryNodeId)));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Boundary(body, variables: [TotalVariable(100, captured: true)])));

        run.AssertWorkflowCompleted();
        Assert.Equal(7, ReadInput(run, "node-read-total"));
        var writes = run.State("node-write-capture").VariableWrites!;
        Assert.Equal(2, writes.Count);
        var session = Assert.Single(writes, write => write.Writer.Source == VariableWriteSource.SessionChange);
        var capture = Assert.Single(writes, write => write.Writer.Source == VariableWriteSource.Capture);
        Assert.DoesNotContain(writes, write => write.IsLostUpdate);
        Assert.Equal(session.NewVersion, capture.CurrentVersion);
        Assert.Equal(session.NewVersion + 1, capture.NewVersion);
    }

    [Fact]
    public async Task Two_captures_of_one_completion_into_one_variable_judge_in_order()
    {
        // T002, FR-007: each capture sees the row as the previous one left it: two records with consecutive versions.
        await using var harness = NewHarness();
        var leaf = Leaf("node-two", typeof(TwoValuesActivity), new Dictionary<string, RuntimeInputBinding>());
        leaf = CaptureInto(leaf, "total", ProjectionKey(leaf, nameof(TwoValues.A)));
        leaf = CaptureInto(leaf, "total", ProjectionKey(leaf, nameof(TwoValues.B)));
        var body = GraphBoundaryCompleteTests.Sequence("node-body", leaf, Echo("node-read-total", Variable("total", BoundaryNodeId)));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Boundary(body, variables: [TotalVariable(100, captured: true)])));

        run.AssertWorkflowCompleted();
        Assert.Equal(2, ReadInput(run, "node-read-total"));
        var writes = run.State("node-two").VariableWrites!.OrderBy(write => write.NewVersion).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(writes[0].NewVersion, writes[1].CurrentVersion);
        Assert.Equal(writes[0].NewVersion + 1, writes[1].NewVersion);
        Assert.DoesNotContain(writes, write => write.IsLostUpdate);
    }

    [Fact]
    public async Task Writing_the_stored_value_back_writes_nothing()
    {
        // US2 AS6: no record, the version stays.
        await using var harness = NewHarness();
        var body = TotalLeaf("node-set", typeof(SetTotalToFiveActivity));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Boundary(body, variables: [TotalVariable(5)])));

        run.AssertWorkflowCompleted();
        Assert.Null(run.State("node-set").VariableWrites);
        Assert.False((await TotalRowAsync(harness, run)).Metadata.ContainsKey(RuntimeMetadataKeys.RowVariableWriter));
    }

    [Fact]
    public async Task A_step_that_writes_and_then_faults_saves_nothing()
    {
        // US2 AS5.
        await using var harness = NewHarness();
        var body = TotalLeaf("node-set", typeof(SetTotalThenThrowActivity));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Boundary(body, variables: [TotalVariable(100)])),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-set").Status);
        Assert.Equal(100, (await TotalRowAsync(harness, run)).InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task A_write_of_the_wrong_type_faults_the_step()
    {
        // Edge case: as in a workflow.
        await using var harness = NewHarness();
        var body = Script("node-js", "variables.Total = 'not a number'; return 1;");

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Boundary(body, variables: [TotalVariable(100)])),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-js").Status);
        Assert.Equal(100, (await TotalRowAsync(harness, run)).InlineValue!.Value.GetInt32());
    }

    // A capture in these tests writes an "object" value, so a captured variable is declared as one.
    [Fact]
    public async Task A_write_before_a_suspension_commits_with_it_and_the_resumed_evaluation_reads_it()
    {
        // US2 AS4.
        await using var harness = NewHarness();
        var leaf = TotalLeaf("node-wait", typeof(WriteThenSuspendActivity));
        var body = GraphBoundaryCompleteTests.Sequence("node-body", leaf, Echo("node-read-total", Variable("total", BoundaryNodeId)));
        var executable = WithResumeTarget(Boundary(body, variables: [TotalVariable(100)]), "node-wait");

        var suspended = await harness.RunAsync(executable, allowPendingWorkOnTerminalCompletion: true);
        Assert.Equal(ActivityExecutionStatus.Suspended, suspended.State("node-wait").Status);
        Assert.Equal(5, (await TotalRowAsync(harness, suspended)).InlineValue!.Value.GetInt32());
        Assert.Equal(VariableWriteSource.SessionChange, Assert.Single(suspended.State("node-wait").VariableWrites!).Writer.Source);

        var resumed = await ResumeAsync(harness, executable, suspended, "node-wait");

        resumed.AssertWorkflowCompleted();
        Assert.Equal(6, ReadInput(resumed, "node-read-total"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_resumed_write_on_a_base_another_branch_moved_is_a_lost_update(bool fault)
    {
        // US2 AS2: the left branch read total (100) as its input and suspended; the right branch made it 101. The resumed
        // write of Seen + 10 is built on the older version: the last commit wins with a warning, or under Fault the step
        // faults and nothing it staged is saved, as for a frame.
        await using var harness = NewHarness();
        var left = Leaf("node-left", typeof(ResumeWriteFromInputActivity), new Dictionary<string, RuntimeInputBinding>
        {
            ["Seen"] = new("Seen", new ValueTypeDescriptor("Int32"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead,
                variable: new RuntimeVariableReference("total", BoundaryNodeId)),
            ["Total"] = TotalLeaf("unused", typeof(SetTotalToFiveActivity)).InputBindings["Total"]
        }, fault ? new Dictionary<string, string> { [RuntimeMetadataKeys.LostUpdateMode] = RuntimeMetadataKeys.LostUpdateModeFault } : null);
        var right = TotalLeaf("node-right", typeof(IncrementTotalActivity));
        var parallel = Nodes.Structural(
            "node-parallel",
            typeof(ParallelActivity),
            ParallelActivity.StructureKind,
            ParallelActivity.StructureSchemaVersion,
            new { branches = new[] { new { name = "left", activity = "node-left" }, new { name = "right", activity = "node-right" } } },
            [new ExecutableChildSlot(ParallelActivity.BranchSlotName("left"), [left]), new ExecutableChildSlot(ParallelActivity.BranchSlotName("right"), [right])]);
        var executable = WithResumeTarget(Boundary(parallel, variables: [TotalVariable(100)]), "node-left");

        var suspended = await harness.RunAsync(executable, allowPendingWorkOnTerminalCompletion: true);
        Assert.Equal(ActivityExecutionStatus.Suspended, suspended.State("node-left").Status);
        Assert.Equal(101, (await TotalRowAsync(harness, suspended)).InlineValue!.Value.GetInt32());

        var resumed = await ResumeAsync(harness, executable, suspended, "node-left");

        var write = Assert.Single(resumed.State("node-left").VariableWrites!);
        Assert.Equal(VariableWriteSource.SessionChange, write.Writer.Source);
        Assert.Equal("node-right", write.OverwrittenWriter!.ExecutableNodeId);
        Assert.Equal(write.BaseVersion + 1, write.CurrentVersion);
        if (fault)
        {
            Assert.Equal(VariableCommitOutcome.LostUpdateFault, write.Outcome);
            Assert.Equal(ActivityExecutionStatus.Faulted, resumed.State("node-left").Status);
            Assert.Equal("VariableLostUpdate", resumed.State("node-left").SubStatus);
            Assert.Equal(101, (await TotalRowAsync(harness, resumed)).InlineValue!.Value.GetInt32());
        }
        else
        {
            Assert.Equal(VariableCommitOutcome.LostUpdateWarning, write.Outcome);
            Assert.Equal(101, write.OverwrittenValue!.InlineValue!.Value.GetInt32());
            resumed.AssertWorkflowCompleted();
            Assert.Equal(110, (await TotalRowAsync(harness, resumed)).InlineValue!.Value.GetInt32());
        }
    }

    private static WorkflowExecutable WithResumeTarget(ExecutableNode root, string waitingNodeId)
    {
        var target = WorkflowExecutableResumeTarget.ComposeScopedId(waitingNodeId, WaitResume.ResumeTargetKey);
        return new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                [target] = new(target, waitingNodeId, "ResumeAsync", new Dictionary<string, string>(), WaitResume.ResumeTargetKey)
            },
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            workflowVariables: [CallerTotal()]);
    }

    private static async Task<WorkflowExecutionRun> ResumeAsync(
        WorkflowExecutionHarness harness, WorkflowExecutable executable, WorkflowExecutionRun suspended, string waitingNodeId)
    {
        var waiting = suspended.State(waitingNodeId);
        return await harness.ResumeAsync(
            executable.Identity,
            bookmarkId: Assert.Single(waiting.BookmarkIds),
            activityExecutionId: waiting.InvocationId,
            executableNodeId: waitingNodeId,
            resumeTargetId: WorkflowExecutableResumeTarget.ComposeScopedId(waitingNodeId, WaitResume.ResumeTargetKey),
            stimulusType: WaitResume.StimulusType,
            stimulusHash: WaitResume.StimulusHash,
            input: JsonSerializer.SerializeToElement(new WaitTrigger(true)));
    }

    private static GraphActivityVariableDescriptor TotalVariable(int initial, bool captured = false) =>
        new("total", "Total", captured ? ObjectType : Int32Type, WellKnownRuntimeDurableValueStorageDrivers.Json,
            new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement(initial)));

    private static async Task<IReadOnlyCollection<DurableValueState>> RowsAsync(WorkflowExecutionHarness harness, WorkflowExecutionRun run) =>
        await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(run.WorkflowState!.WorkflowExecutionId);

    private static async Task<DurableValueState> TotalRowAsync(WorkflowExecutionHarness harness, WorkflowExecutionRun run, string boundary = BoundaryNodeId)
    {
        var boundaryState = run.State(boundary);
        return Assert.Single(await RowsAsync(harness, run), value =>
            value.Metadata.GetValueOrDefault(RuntimeMetadataKeys.BoundaryValueRole) == ActivityExecutionScopeValues.VariableRole &&
            value.Metadata.GetValueOrDefault(RuntimeMetadataKeys.BoundaryReferenceKey) == "total" &&
            value.SourceActivityExecutionId == boundaryState.InvocationId);
    }

    private static string ProjectionKey(ExecutableNode node, string output) =>
        node.ActivityContract!.Result.Projections.Keys.Single(key => key.EndsWith(output, StringComparison.Ordinal));

    private static ExecutableNode TotalLeaf(string nodeId, Type activityType, string scopeId = BoundaryNodeId) =>
        Leaf(nodeId, activityType, new Dictionary<string, RuntimeInputBinding>
        {
            ["Total"] = new(
                "Total",
                new ValueTypeDescriptor(VariableReferenceTypes.Alias),
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(
                    new ValueTypeDescriptor(VariableReferenceTypes.Alias),
                    JsonSerializer.SerializeToElement(new { name = "Total", scopeId, variableKey = "total" }),
                    ValueProtectionPolicy.InstanceInline))
        });

    private static ExecutableNode Leaf(string nodeId, Type activityType, IReadOnlyDictionary<string, RuntimeInputBinding> bindings,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(activityType);
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: bindings,
            metadata: metadata ?? new Dictionary<string, string>(),
            activityContract: contract);
    }

    private static ExecutableNode Script(string nodeId, string script) => new(
        executableNodeId: nodeId,
        authoredActivityId: $"authored-{nodeId}",
        activityType: typeof(RunJavaScript).FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding>
        {
            ["script"] = ScriptLiteral("script", "String", JsonSerializer.SerializeToElement(script)),
            ["arguments"] = ScriptLiteral("arguments", "JsonElement", JsonSerializer.SerializeToElement(new Dictionary<string, string>()))
        },
        // Placement pins a body script's workflow-state access off: a closed body has no workflow state.
        metadata: new Dictionary<string, string> { ["accessesWorkflowState"] = "false" });

    private static RuntimeInputBinding ScriptLiteral(string key, string alias, JsonElement value)
    {
        var type = new ValueTypeDescriptor(alias);
        return new RuntimeInputBinding(key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, value, ValueProtectionPolicy.InstanceInline));
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesScriptingFeature().ConfigureServices(services))
            .WithFeature(services => new GraphActivitiesRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new Expressions.ExpressionsFeature().ConfigureServices(services);
                new Expressions.JavaScript.JavaScriptFeature().ConfigureServices(services);
                new Expressions.JavaScript.Jint.JintFeature().ConfigureServices(services);
            })
            .Build(Enumerable.Range(1, 64).Select(index => $"actexec-{index}"));

    public sealed record TotalResult([property: Output(Key = "Value", Path = "value")] int Value);

    public sealed record TwoValues(
        [property: Output(Key = "A", Path = "a")] int A,
        [property: Output(Key = "B", Path = "b")] int B);

    /// <summary>Sets the bound <c>Total</c> to 5 without reading it.</summary>
    public sealed class SetTotalToFiveActivity : Activity
    {
        [ActivityInput] public VariableReference<int> Total { get; set; } = default!;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            context.Variables.Set(Total, 5);
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
        }
    }

    /// <summary>Reads the bound <c>Total</c> and writes it plus one.</summary>
    public sealed class IncrementTotalActivity : Activity
    {
        [ActivityInput] public VariableReference<int> Total { get; set; } = default!;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            context.Variables.Set(Total, context.Variables.Get(Total) + 1);
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
        }
    }

    /// <summary>Marked for free names: increments <c>Total</c> by name.</summary>
    [AccessesUndeclaredVariables]
    public sealed class IncrementTotalByNameActivity : Activity
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            context.Variables.Set("Total", context.Variables.Get<int>("Total") + 1);
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
        }
    }

    /// <summary>Writes 5 to <c>Total</c> through its session and returns 7, which a capture writes into <c>Total</c>.</summary>
    public sealed class WriteFiveReturnSevenActivity : Activity<TotalResult>
    {
        [ActivityInput] public VariableReference<int> Total { get; set; } = default!;

        protected override ValueTask<ActivityTransition<TotalResult>> ExecuteAsync(ActivityExecutionContext context)
        {
            context.Variables.Set(Total, 5);
            return ValueTask.FromResult(ActivityTransition.Complete(new TotalResult(7)));
        }
    }

    /// <summary>Returns two values, both captured into <c>Total</c>.</summary>
    public sealed class TwoValuesActivity : Activity<TwoValues>
    {
        protected override ValueTask<ActivityTransition<TwoValues>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new TwoValues(1, 2)));
    }

    public sealed record WaitState(string Phase);

    public sealed record WaitTrigger(bool Go);

    public static class WaitResume
    {
        public const string ResumeTargetKey = "body-wait";
        public const string StimulusType = "TestBodyWait";
        public const string StimulusHash = "body-wait:1";

        public static IReadOnlyCollection<ActivityTriggerRegistration<WaitTrigger>> Registration => [new(ResumeTargetKey, StimulusType, StimulusHash)];
    }

    /// <summary>Writes 5 to <c>Total</c> and suspends; on resume reads <c>Total</c> through a fresh session and writes it plus one.</summary>
    public sealed class WriteThenSuspendActivity : StatefulActivity<ActivityUnit, WaitState, WaitTrigger>
    {
        [ActivityInput] public VariableReference<int> Total { get; set; } = default!;

        protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ExecuteAsync(ActivityExecutionContext context)
        {
            context.Variables.Set(Total, 5);
            return ValueTask.FromResult(Suspend(new WaitState("waiting"), WaitResume.Registration));
        }

        protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context)
        {
            context.Variables.Set(Total, context.Variables.Get(Total) + 1);
            return ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
        }
    }

    /// <summary>Suspends; on resume writes <c>Seen + 10</c>, computed from its input read of <c>Total</c>.</summary>
    public sealed class ResumeWriteFromInputActivity : StatefulActivity<ActivityUnit, WaitState, WaitTrigger>
    {
        [ActivityInput] public int Seen { get; set; }

        [ActivityInput] public VariableReference<int> Total { get; set; } = default!;

        protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(Suspend(new WaitState("waiting"), WaitResume.Registration));

        protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context)
        {
            context.Variables.Set(Total, Seen + 10);
            return ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
        }
    }

    /// <summary>Writes <c>Total</c> and then throws.</summary>
    public sealed class SetTotalThenThrowActivity : Activity
    {
        [ActivityInput] public VariableReference<int> Total { get; set; } = default!;

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            context.Variables.Set(Total, 9);
            throw new InvalidOperationException("stop");
        }
    }
}
