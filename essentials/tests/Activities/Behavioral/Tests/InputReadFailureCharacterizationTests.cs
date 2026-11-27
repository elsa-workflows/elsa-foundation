using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// Characterises, through the real engine on main before #4078, how a failed input read surfaces, whether a typed
/// result can commit as null, and what a drain does when its pinned executable cannot be read. #4078's member-selection
/// failures are designed to surface exactly like the causal failures pinned here.
/// </summary>
public sealed class InputReadFailureCharacterizationTests
{
    private const string Key = nameof(Echo.Value);

    /// <summary>
    /// A required producer that never ran, and one that ran twice in the reader's lineage, both fail the reader's input
    /// read inside the StartActivity handler. The reader is never started and records no input snapshot, the run stays
    /// open, the work item is poisoned without a retry, and one blocking incident names the reader. The incident's type
    /// is the scheduler's poison type; the causal reason lives only in its message and metadata text.
    /// </summary>
    [Theory]
    [InlineData("unavailable")]
    [InlineData("ambiguous")]
    public async Task A_failed_causal_input_read_blocks_the_reader_with_one_poison_incident_before_it_starts(string failure)
    {
        await using var harness = NewHarness(failure == "ambiguous" ? ["again", "done"] : []);
        var root = failure == "ambiguous"
            ? Flowchart("node-flowchart",
                [Leaf("node-a", typeof(Stamp)), Leaf("node-b", typeof(Stamp)), Leaf("node-c", typeof(LoopingEcho), Result("node-b", Key))],
                [Connect("node-a", "node-b"), Connect("node-b", "node-c"), Connect("node-c", "node-a", "again")],
                "node-a")
            : GraphBoundaryCompleteTests.Sequence("node-root", Leaf("node-r", typeof(EchoActivity), Result("node-missing", Key)));
        var reader = failure == "ambiguous" ? "node-c" : "node-r";

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root),
            allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        var blocked = run.States(reader).OrderBy(state => state.ExecutionSequence).Last();
        Assert.Equal(ActivityExecutionStatus.Scheduled, blocked.Status);
        Assert.Null(blocked.InputSnapshot);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);

        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.True(incident.IsBlocking);
        Assert.Equal(blocked.InvocationId, incident.ActivityExecutionId);
        Assert.Equal(reader, incident.ExecutableNodeId);
        Assert.Equal("SchedulerWorkPoisoned", incident.FailureType);
        var reason = failure == "ambiguous" ? "is ambiguous for consumer invocation" : "is not available in the causal lineage";
        Assert.Contains(reason, incident.Message, StringComparison.Ordinal);
        Assert.Equal("Elsa.Workflows.Runtime.Core.Exceptions.CausalActivityResultResolutionException", incident.Metadata["runtime.faultType"]);
        Assert.Contains(reason, incident.Metadata["runtime.faultMessage"], StringComparison.Ordinal);
        Assert.Equal("StartActivity", incident.Metadata["runtime.commandKind"]);
        Assert.Equal("WorkflowStartActivitySchedulerWorkHandler", incident.Metadata["runtime.poison.handlerName"]);
        Assert.Equal("DoNotRetry", incident.Metadata["runtime.poison.retryMode"]);

        var poison = Assert.Single(await harness.Services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, poison.Disposition);
        Assert.Equal(WorkflowExecutionCommandKind.StartActivity, poison.CommandKind);
    }

    /// <summary>
    /// A CLR activity that declares a typed result cannot complete with null: completion refuses it (VF-ACT-006), the
    /// producer faults and its reader never runs, whether it binds the whole result or a declared projection. So the
    /// non-null root the CLR shape source assumes for a whole result (#4078 note, section 4) holds for these producers.
    /// </summary>
    [Theory]
    [InlineData("$result")]
    [InlineData(Key)]
    public async Task A_typed_result_cannot_be_committed_as_null_so_its_reader_never_runs(string projection)
    {
        await using var harness = NewHarness([]);
        var root = GraphBoundaryCompleteTests.Sequence("node-root",
            Leaf("node-p", typeof(NullProducer)), Leaf("node-r", typeof(EchoActivity), Result("node-p", projection)));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root),
            allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState!.Status);
        var producer = run.State("node-p");
        Assert.Equal(ActivityExecutionStatus.Faulted, producer.Status);
        Assert.Null(producer.Completion);
        Assert.Contains("VF-ACT-006", producer.Fault!.Message, StringComparison.Ordinal);
        Assert.False(run.DidRun("node-r"));
    }

    /// <summary>
    /// Stands in for an older runtime reading an executable it cannot deserialize. When a drain's pinned executable read
    /// throws what the EF store throws for an unreadable payload (InvalidDataException, "payload is corrupt"), the work
    /// item it was handling is parked as poisoned without a retry and one blocking incident is raised; nothing loops.
    /// Which item fails depends on how many reads succeed first, and with it the run's status: a failed start or
    /// checkpoint faults the run, a failed child schedule leaves it running and blocked. This proves what this drain
    /// does with a throwing read, not how a real older build behaves.
    /// </summary>
    [Theory]
    [InlineData(1, "Start", WorkflowExecutionStatus.Faulted)]
    [InlineData(2, "Checkpoint", WorkflowExecutionStatus.Faulted)]
    [InlineData(3, "ScheduleActivity", WorkflowExecutionStatus.Running)]
    public async Task A_drain_whose_pinned_executable_read_throws_poisons_that_work_item_without_a_retry(
        int successfulReads, string commandKind, WorkflowExecutionStatus status)
    {
        var reads = new ReadBudget(successfulReads);
        await using var harness = NewHarness([], services =>
        {
            services.AddSingleton(reads);
            services.AddScoped<IWorkflowExecutableReader, CorruptExecutableReader>();
        });
        var root = GraphBoundaryCompleteTests.Sequence("node-root", Leaf("node-a", typeof(Stamp)));

        var exception = await Record.ExceptionAsync(() => harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root),
            allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true));

        Assert.Null(exception);
        var run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Equal(status, run.WorkflowState!.Status);
        var poison = Assert.Single(await harness.Services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(commandKind, poison.CommandKind.ToString());
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, poison.Disposition);
        Assert.Equal(typeof(InvalidDataException).FullName, poison.Fault.ExceptionType);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.True(incident.IsBlocking);
        Assert.Equal("SchedulerWorkPoisoned", incident.FailureType);
    }

    /// <summary>How many executable reads succeed before the reader starts throwing.</summary>
    public sealed class ReadBudget(int reads)
    {
        private int _remaining = reads;

        public bool Take() => Interlocked.Decrement(ref _remaining) >= 0;
    }

    /// <summary>Serves the first reads from the store, then throws what the EF store throws for an unreadable payload.</summary>
    private sealed class CorruptExecutableReader(ReadBudget budget, IWorkflowExecutableStore store) : IWorkflowExecutableReader
    {
        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default) =>
            budget.Take()
                ? store.FindAsync(artifactId, cancellationToken)
                : throw new InvalidDataException("The persisted workflow executable payload is corrupt.",
                    new JsonException("The JSON value could not be converted to RuntimeInputBindingSource."));
    }

    private static WorkflowExecutionHarness NewHarness(string[] loopOutcomes, Action<IServiceCollection>? configure = null)
    {
        var builder = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => services.AddSingleton(new ActivityResultOccurrenceTests.LoopOutcomes(loopOutcomes)))
            .WithProbeLeaf();
        if (configure is not null)
            builder = builder.ConfigureServices(configure);
        return builder.Build(Enumerable.Range(1, 64).Select(index => $"actexec-{index}"));
    }

    private static RuntimeInputBinding Result(string producerNodeId, string projection) => new(
        Key, new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.ActivityResult,
        activityResult: new RuntimeActivityResultReference(producerNodeId, projection, producerScopeId: null));

    private static ExecutableNode Leaf(string nodeId, Type activityType, RuntimeInputBinding? read = null) => new(
        executableNodeId: nodeId,
        authoredActivityId: $"authored-{nodeId}",
        activityType: activityType.FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: read is null
            ? new Dictionary<string, RuntimeInputBinding>()
            : new Dictionary<string, RuntimeInputBinding> { [read.InputName] = read },
        metadata: new Dictionary<string, string>());

    private static FlowchartConnection Connect(string source, string target, string? outcome = null) =>
        new(new FlowchartEndpoint(source, outcome), new FlowchartEndpoint(target));

    private static ExecutableNode Flowchart(string nodeId, IReadOnlyCollection<ExecutableNode> children,
        IReadOnlyCollection<FlowchartConnection> connections, string startNodeId) => new(
        executableNodeId: nodeId,
        authoredActivityId: $"authored-{nodeId}",
        activityType: typeof(FlowchartActivity).FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding>(),
        metadata: new Dictionary<string, string>(),
        childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, children)],
        structure: new ExecutableActivityStructure(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new FlowchartStructure(connections, startNodeId, null))));

    public sealed record Echo([property: Output(Key = "Value", Path = "value")] object? Value);

    /// <summary>Returns its own activity execution id.</summary>
    public sealed class Stamp : Activity<Echo>
    {
        protected override ValueTask<ActivityTransition<Echo>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new Echo(context.InvocationId)));
    }

    /// <summary>Declares a typed result and completes with null.</summary>
    public sealed class NullProducer : Activity<Echo?>
    {
        protected override ValueTask<ActivityTransition<Echo?>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete<Echo?>(null));
    }

    /// <summary>Returns its input.</summary>
    public sealed class EchoActivity : Activity<Echo>
    {
        [ActivityInput] public object? Value { get; set; }

        protected override ValueTask<ActivityTransition<Echo>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new Echo(Value)));
    }

    /// <summary>Returns its input and closes a loop with the run's next scripted outcome.</summary>
    [ActivityOutcome("again")]
    [ActivityOutcome("done")]
    public sealed class LoopingEcho(ActivityResultOccurrenceTests.LoopOutcomes outcomes) : Activity<Echo>
    {
        [ActivityInput] public object? Value { get; set; }

        protected override ValueTask<ActivityTransition<Echo>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new Echo(Value), outcomes.Next()));
    }
}
