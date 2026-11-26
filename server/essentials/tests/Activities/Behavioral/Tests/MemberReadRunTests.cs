using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// Member reads (#4078 slice B) through the real engine with hand-built executables. A reader bound to one member of an
/// activity result, a variable or a workflow request member commits exactly that member as its input, in a Sequence
/// and in a top-level Flowchart, with pinned or deferred facts. A failed read never constructs the reader, writes no
/// input snapshot and raises one blocking incident carrying its VF-SEL code. A member read picks the same producer run
/// a whole-value read does. A runtime that does not select members refuses the executable before any activity runs.
/// </summary>
public sealed class MemberReadRunTests
{
    private const string Key = nameof(Reader.Value);
    private static readonly ValueTypeDescriptor Text = new("String");

    public static TheoryData<string, bool> Structures => new()
    {
        { "sequence", false }, { "sequence", true }, { "flowchart", false }, { "flowchart", true }
    };

    [Theory]
    [MemberData(nameof(Structures))]
    public async Task A_reader_of_an_activity_result_member_commits_exactly_that_member(string structure, bool known)
    {
        await using var harness = NewHarness();
        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            Root(structure, Leaf("node-p", typeof(CustomerProducer)), Leaf("node-r", typeof(Reader), ResultMember("node-p", ["address", "street"], known)))));

        run.AssertWorkflowCompleted();
        Assert.Equal("Main", run.AssertCompleted("node-r").InputSnapshot!.Values[Key].InlineValue!.Value.GetString());
        Assert.Equal("Main", Output(run, "node-r"));
    }

    [Theory]
    [MemberData(nameof(Structures))]
    public async Task A_reader_of_a_variable_member_commits_exactly_that_member(string structure, bool known)
    {
        await using var harness = NewHarness();
        var variable = new RuntimeVariableDeclaration("customer", "customer", new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
            new RuntimeInputBinding("customer", new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(new ValueTypeDescriptor("Object"), CustomerJson, ValueProtectionPolicy.InstanceInline)));
        var read = new RuntimeInputBinding(Key, Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableMember, Selection(["customerId"], known),
            variable: new RuntimeVariableReference("customer", VariableReference.WorkflowScopeId));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            Root(structure, Leaf("node-p", typeof(CustomerProducer)), Leaf("node-r", typeof(Reader), read)), variable));

        run.AssertWorkflowCompleted();
        Assert.Equal("c-1", run.AssertCompleted("node-r").InputSnapshot!.Values[Key].InlineValue!.Value.GetString());
    }

    [Theory]
    [MemberData(nameof(Structures))]
    public async Task A_reader_of_a_workflow_request_member_commits_exactly_that_member(string structure, bool known)
    {
        await using var harness = NewHarness();
        var read = new RuntimeInputBinding(Key, Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequestMember,
            Selection(["display_name"], known), workflowRequest: new RuntimeWorkflowRequestReference("customer"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
                Root(structure, Leaf("node-p", typeof(CustomerProducer)), Leaf("node-r", typeof(Reader), read))),
            new Dictionary<string, JsonElement> { ["customer"] = CustomerJson });

        run.AssertWorkflowCompleted();
        Assert.Equal("Ann", run.AssertCompleted("node-r").InputSnapshot!.Values[Key].InlineValue!.Value.GetString());
    }

    public static TheoryData<string[], string> Failures => new()
    {
        { ["missing"], "VF-SEL-001" },
        { ["nickname"], "VF-SEL-002" },
        { ["tags", "0"], "VF-SEL-003" },
        { ["address"], "VF-SEL-004" }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_failed_member_read_never_constructs_its_reader_and_raises_one_incident_with_its_code(string[] segments, string code)
    {
        var constructions = new ReaderConstructions();
        await using var harness = NewHarness(services => services.AddSingleton(constructions));
        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(GraphBoundaryCompleteTests.Sequence("node-root",
                Leaf("node-p", typeof(CustomerProducer)), Leaf("node-r", typeof(CountingReader), ResultMember("node-p", segments, known: false)))),
            allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        var producer = run.AssertCompleted("node-p");
        Assert.Equal("c-1", producer.Completion!.Result.InlineValue!.Value.GetProperty("customerId").GetString());
        var reader = run.State("node-r");
        Assert.Equal(ActivityExecutionStatus.Scheduled, reader.Status);
        Assert.Null(reader.InputSnapshot);
        Assert.Equal(0, constructions.Count);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.True(incident.IsBlocking);
        Assert.Equal("node-r", incident.ExecutableNodeId);
        Assert.Equal("SchedulerWorkPoisoned", incident.FailureType);
        Assert.Equal(code, incident.Metadata["runtime.inputFailureCode"]);
        Assert.Equal(Key, incident.Metadata["runtime.inputKey"]);
        Assert.StartsWith(code + ": ", incident.Metadata["runtime.faultMessage"], StringComparison.Ordinal);
    }

    /// <summary>
    /// A member read resolves its producer occurrence exactly as the whole-value read of the same result: under a
    /// first-wins fork both resolve; on a Flowchart loop without a scope both read the first pass and then block as
    /// Ambiguous on the second (#4056, unchanged).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_member_read_picks_the_same_producer_run_as_the_whole_value_read(bool loop)
    {
        var whole = await OccurrenceRunAsync(loop, member: false);
        var member = await OccurrenceRunAsync(loop, member: true);

        Assert.Single(whole.Reads);
        Assert.StartsWith("actexec-", whole.Reads[0], StringComparison.Ordinal);
        Assert.Equal(loop, whole.Blocked);
        if (loop)
            Assert.Equal(typeof(CausalActivityResultResolutionException).FullName, whole.FaultType);
        Assert.Equal(whole.Reads, member.Reads);
        Assert.Equal(whole.Blocked, member.Blocked);
        Assert.Equal(whole.FaultType, member.FaultType);
    }

    private static async Task<(IReadOnlyList<string?> Reads, bool Blocked, string? FaultType)> OccurrenceRunAsync(bool loop, bool member)
    {
        await using var harness = NewHarness(loopOutcomes: loop ? ["again", "done"] : []);
        RuntimeInputBinding Read(string producer) => member
            ? new(Key, new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResultMember,
                Selection(["value"], known: false), activityResult: new RuntimeActivityResultReference(producer, "$result", producerScopeId: null))
            : new(Key, new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
                activityResult: new RuntimeActivityResultReference(producer, "Value", producerScopeId: null));
        var root = loop
            ? Flowchart([Leaf("node-a", typeof(Stamp)), Leaf("node-b", typeof(Stamp)), Leaf("node-c", typeof(LoopingReader), Read("node-b")), Leaf("node-z", typeof(Stamp))],
                [Connect("node-a", "node-b"), Connect("node-b", "node-c"), Connect("node-c", "node-a", "again"), Connect("node-c", "node-z", "done")], "node-a")
            : Flowchart([Leaf("node-fork", typeof(Stamp)), Leaf("node-p", typeof(Stamp)), Leaf("node-q", typeof(Stamp)), Leaf("node-r", typeof(Reader), Read("node-p"))],
                [Connect("node-fork", "node-p"), Connect("node-fork", "node-q"), Connect("node-p", "node-r")], "node-fork",
                new Dictionary<string, FlowchartNodeMetadata> { ["node-fork"] = new("firstWins") });
        var reader = loop ? "node-c" : "node-r";

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root), allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        var reads = run.States(reader).Where(state => state.Status == ActivityExecutionStatus.Completed).OrderBy(state => state.ExecutionSequence)
            .Select(state => state.InputSnapshot!.Values[Key].InlineValue!.Value.GetString()).ToArray();
        var incident = (await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId)).SingleOrDefault();
        return (reads, incident is not null, incident?.Metadata.GetValueOrDefault("runtime.faultType"));
    }

    /// <summary>
    /// The input snapshot records the selected member, and a suspended reader's resume works from that record: a
    /// source changed while the reader waited is not selected again.
    /// </summary>
    [Fact]
    public async Task A_resumed_reader_keeps_the_member_it_selected_before_it_suspended()
    {
        var seen = new SeenValues();
        await using var harness = NewHarness(services => services.AddSingleton(seen));
        var resumeTarget = WorkflowExecutableResumeTarget.ComposeScopedId("node-wait", WaitingReader.ResumeTargetKey);
        var executable = Waiting(resumeTarget);

        var suspended = await harness.RunAsync(executable, allowPendingWorkOnTerminalCompletion: true);
        var wait = suspended.State("node-wait");
        Assert.Equal("Main", wait.InputSnapshot!.Values[Key].InlineValue!.Value.GetString());

        var states = harness.Services.GetRequiredService<IActivityExecutionStateStore>();
        var producer = suspended.State("node-p");
        var completion = producer.Completion!;
        await states.SaveAsync(producer with
        {
            Completion = new ActivityCompletion(completion.InvocationId, completion.AttemptId,
                ValueEnvelope.Inline(completion.Result.Type, JsonSerializer.SerializeToElement(new { address = new { street = "Changed" } }), completion.Result.Policy),
                completion.OutcomeKey, completion.CompletedAt, completion.ContractFingerprint)
        });

        var resumed = await harness.ResumeAsync(WorkflowExecutionHarness.Identity, Assert.Single(wait.BookmarkIds), wait.InvocationId, "node-wait",
            resumeTarget, WaitingReader.StimulusType, WaitingReader.StimulusHash, JsonSerializer.SerializeToElement(new WaitTrigger(true)));

        resumed.AssertWorkflowCompleted();
        Assert.Equal(["Main", "Main"], seen.Values);
    }

    /// <summary>
    /// A runtime that does not advertise member selection refuses an executable with a member read before any activity
    /// runs: on start the root never starts, and on resume the waiting reader's bookmark is not consumed.
    /// </summary>
    [Fact]
    public async Task Without_the_capability_a_member_executable_is_refused_before_any_activity_starts()
    {
        var checker = new SwitchableChecker();
        await using var harness = NewHarness(services => services.AddScoped<IRuntimeRequirementChecker>(provider =>
            checker.Wrap(ActivatorUtilities.CreateInstance<Elsa.Workflows.Runtime.Services.Executables.RuntimeRequirementChecker>(provider))));
        checker.MemberSelectionAvailable = false;

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(GraphBoundaryCompleteTests.Sequence("node-root",
                Leaf("node-p", typeof(CustomerProducer)), Leaf("node-r", typeof(Reader), ResultMember("node-p", ["customerId"], known: false)))),
            allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        Assert.DoesNotContain(run.ActivityStates, state => state.Status is ActivityExecutionStatus.Running or ActivityExecutionStatus.Completed);
        Assert.False(run.DidRun("node-p"));
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.True(incident.IsBlocking);
        Assert.Contains("elsa.runtime.input-member-selection@1", incident.Message, StringComparison.Ordinal);
        Assert.Contains("none of it runs here", incident.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_capability_a_waiting_member_executable_is_not_resumed()
    {
        var seen = new SeenValues();
        var checker = new SwitchableChecker();
        await using var harness = NewHarness(services =>
        {
            services.AddSingleton(seen);
            services.AddScoped<IRuntimeRequirementChecker>(provider =>
                checker.Wrap(ActivatorUtilities.CreateInstance<Elsa.Workflows.Runtime.Services.Executables.RuntimeRequirementChecker>(provider)));
        });
        var resumeTarget = WorkflowExecutableResumeTarget.ComposeScopedId("node-wait", WaitingReader.ResumeTargetKey);
        var suspended = await harness.RunAsync(Waiting(resumeTarget), allowPendingWorkOnTerminalCompletion: true);
        var wait = suspended.State("node-wait");

        checker.MemberSelectionAvailable = false;
        await Record.ExceptionAsync(() => harness.ResumeAsync(WorkflowExecutionHarness.Identity, Assert.Single(wait.BookmarkIds), wait.InvocationId,
            "node-wait", resumeTarget, WaitingReader.StimulusType, WaitingReader.StimulusHash, JsonSerializer.SerializeToElement(new WaitTrigger(true))));

        var after = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Equal(ActivityExecutionStatus.Suspended, after.State("node-wait").Status);
        Assert.Equal(["Main"], seen.Values);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Contains("elsa.runtime.input-member-selection@1", incident.Message, StringComparison.Ordinal);
        // The refused resume consumed nothing: the bookmark is still there to resume once the runtime selects members.
        Assert.NotNull(await harness.Services.GetRequiredService<IBookmarkStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, Assert.Single(wait.BookmarkIds)));
    }

    /// <summary>
    /// A structural parent that re-materializes its inputs when its child completes (as <c>While</c> does) reads its
    /// member again there, so that read is guarded too: when the runtime stops selecting members while the child runs,
    /// the parent's child-completion evaluation is refused rather than handing the read to the materializer.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rematerializing_parent_is_refused_on_child_completion_without_the_capability(bool disableDuringChild)
    {
        var checker = new SwitchableChecker();
        var reads = new SeenValues();
        await using var harness = NewHarness(services =>
        {
            services.AddSingleton(checker);
            services.AddSingleton(reads);
            services.AddSingleton(new DisableMemberSelection(disableDuringChild));
            services.AddScoped<IRuntimeRequirementChecker>(provider =>
                checker.Wrap(ActivatorUtilities.CreateInstance<Elsa.Workflows.Runtime.Services.Executables.RuntimeRequirementChecker>(provider)));
        });
        var read = new RuntimeInputBinding(Key, Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequestMember,
            Selection(["display_name"], known: false), workflowRequest: new RuntimeWorkflowRequestReference("customer"));
        var parent = new ExecutableNode(
            executableNodeId: "node-parent",
            authoredActivityId: "authored-node-parent",
            activityType: typeof(RematerializingMemberParent).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [Key] = read },
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("Children", [Leaf("node-child", typeof(CapabilityTogglingChild))])]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(parent), allowPendingWorkOnTerminalCompletion: true,
            inputs: new Dictionary<string, JsonElement> { ["customer"] = CustomerJson }, allowAcceptedButFaulted: true);

        run.AssertCompleted("node-child");
        var incidents = await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        if (!disableDuringChild)
        {
            run.AssertWorkflowCompleted();
            Assert.Equal(["Ann", "Ann"], reads.Values);
            Assert.Empty(incidents);
            return;
        }

        Assert.Equal(["Ann"], reads.Values);
        Assert.NotEqual(ActivityExecutionStatus.Completed, run.State("node-parent").Status);
        var incident = Assert.Single(incidents);
        Assert.True(incident.IsBlocking);
        Assert.Contains("elsa.runtime.input-member-selection@1", incident.Message, StringComparison.Ordinal);
        Assert.Contains("none of it runs here", incident.Message, StringComparison.Ordinal);
    }

    private static WorkflowExecutable Waiting(string resumeTarget) => new(WorkflowExecutionHarness.Identity,
        GraphBoundaryCompleteTests.Sequence("node-root", Leaf("node-p", typeof(CustomerProducer)),
            Leaf("node-wait", typeof(WaitingReader), ResultMember("node-p", ["address", "street"], known: false))),
        new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            [resumeTarget] = new(resumeTarget, "node-wait", "ResumeAsync", new Dictionary<string, string>(), WaitingReader.ResumeTargetKey)
        },
        WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);

    private static readonly JsonElement CustomerJson = JsonSerializer.SerializeToElement(new
    {
        customerId = "c-1", display_name = "Ann", address = new { street = "Main" }
    });

    private static RuntimeReadSelection Selection(string[] segments, bool known) => known
        ? new(1, segments, RuntimeReadSelectionMode.Known, Text, mayBeAbsent: false, mayBeNull: false, ValueRepresentation.TextValue, "elsa.activity-value-json/1")
        : new(1, segments, RuntimeReadSelectionMode.DeferredFit);

    private static RuntimeInputBinding ResultMember(string producer, string[] segments, bool known) => new(
        Key, Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResultMember, Selection(segments, known),
        activityResult: new RuntimeActivityResultReference(producer, "$result", producerScopeId: null));

    private static string? Output(WorkflowExecutionRun run, string nodeId) =>
        run.AssertCompleted(nodeId).Completion!.Result.InlineValue!.Value.GetProperty("value").GetString();

    private static ExecutableNode Root(string structure, ExecutableNode producer, ExecutableNode reader) => structure == "sequence"
        ? GraphBoundaryCompleteTests.Sequence("node-root", producer, reader)
        : Flowchart([producer, reader], [Connect(producer.ExecutableNodeId, reader.ExecutableNodeId)], producer.ExecutableNodeId);

    private static WorkflowExecutionHarness NewHarness(Action<IServiceCollection>? configure = null, string[]? loopOutcomes = null)
    {
        var builder = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => services.AddSingleton(new ActivityResultOccurrenceTests.LoopOutcomes(loopOutcomes ?? [])))
            .WithProbeLeaf();
        if (configure is not null)
            builder = builder.ConfigureServices(configure);
        return builder.Build(Enumerable.Range(1, 64).Select(index => $"actexec-{index}"));
    }

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

    private static ExecutableNode Flowchart(IReadOnlyCollection<ExecutableNode> children, IReadOnlyCollection<FlowchartConnection> connections,
        string startNodeId, IReadOnlyDictionary<string, FlowchartNodeMetadata>? metadata = null) => new(
        executableNodeId: "node-flowchart",
        authoredActivityId: "authored-node-flowchart",
        activityType: typeof(FlowchartActivity).FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding>(),
        metadata: new Dictionary<string, string>(),
        childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, children)],
        structure: new ExecutableActivityStructure(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new FlowchartStructure(connections, startNodeId, metadata))));

    public sealed record Address(string Street);

    public sealed record CustomerRecord(string CustomerId, Address Address, string? Nickname, string[] Tags);

    public sealed record Echo([property: Output(Key = "Value", Path = "value")] object? Value);

    /// <summary>Produces a customer whose camelCase serialized members a reader selects from.</summary>
    public sealed class CustomerProducer : Activity<CustomerRecord>
    {
        protected override ValueTask<ActivityTransition<CustomerRecord>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new CustomerRecord("c-1", new Address("Main"), null, ["x"])));
    }

    /// <summary>Returns its own activity execution id under <c>value</c>.</summary>
    public sealed class Stamp : Activity<Echo>
    {
        protected override ValueTask<ActivityTransition<Echo>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new Echo(context.InvocationId)));
    }

    /// <summary>Returns its text input.</summary>
    public sealed class Reader : Activity<Echo>
    {
        [ActivityInput] public object? Value { get; set; }

        protected override ValueTask<ActivityTransition<Echo>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new Echo(Value)));
    }

    public sealed class ReaderConstructions
    {
        private int _count;
        public int Count => _count;
        public void Add() => Interlocked.Increment(ref _count);
    }

    /// <summary>A text reader that counts its constructions, so a test can see it was never built.</summary>
    public sealed class CountingReader : Activity<Echo>
    {
        public CountingReader(ReaderConstructions constructions) => constructions.Add();

        [ActivityInput] public string Value { get; set; } = null!;

        protected override ValueTask<ActivityTransition<Echo>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new Echo(Value)));
    }

    /// <summary>A reader that also closes a loop with the run's next scripted outcome.</summary>
    [ActivityOutcome("again")]
    [ActivityOutcome("done")]
    public sealed class LoopingReader(ActivityResultOccurrenceTests.LoopOutcomes outcomes) : Activity<Echo>
    {
        [ActivityInput] public object? Value { get; set; }

        protected override ValueTask<ActivityTransition<Echo>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new Echo(Value), outcomes.Next()));
    }

    public sealed class SeenValues
    {
        public List<string> Values { get; } = [];
    }

    public sealed record WaitState(string Phase);

    public sealed record WaitTrigger(bool Go);

    /// <summary>Records its input when it executes and again when it resumes.</summary>
    public sealed class WaitingReader(SeenValues seen) : StatefulActivity<ActivityUnit, WaitState, WaitTrigger>
    {
        public const string ResumeTargetKey = "wait";
        public const string StimulusType = "TestMemberWait";
        public const string StimulusHash = "member-wait:1";

        [ActivityInput] public string Value { get; set; } = null!;

        protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ExecuteAsync(ActivityExecutionContext context)
        {
            seen.Values.Add(Value);
            return ValueTask.FromResult(Suspend(new WaitState("waiting"),
                [new ActivityTriggerRegistration<WaitTrigger>(ResumeTargetKey, StimulusType, StimulusHash)]));
        }

        protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context)
        {
            seen.Values.Add(Value);
            return ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
        }
    }

    public sealed record DisableMemberSelection(bool Enabled);

    /// <summary>A child that, when asked to, makes the runtime stop reporting member selection while it runs.</summary>
    public sealed class CapabilityTogglingChild(SwitchableChecker checker, DisableMemberSelection disable) : Activity<Echo>
    {
        protected override ValueTask<ActivityTransition<Echo>> ExecuteAsync(ActivityExecutionContext context)
        {
            if (disable.Enabled)
                checker.MemberSelectionAvailable = false;
            return ValueTask.FromResult(ActivityTransition.Complete(new Echo(context.InvocationId)));
        }
    }

    /// <summary>
    /// A structural parent with a member-read input that re-materializes its inputs on child completion. It records
    /// the input when it starts and again when its child completes, then completes.
    /// </summary>
    public sealed class RematerializingMemberParent(SeenValues reads) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeRematerializeInputsOnChildCompletion
    {
        [ActivityInput] public string Value { get; set; } = null!;

        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            reads.Values.Add(Value);
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            reads.Values.Add(Value);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    /// <summary>Wraps the real checker and can report member selection unavailable, as a runtime without it would.</summary>
    public sealed class SwitchableChecker
    {
        public bool MemberSelectionAvailable { get; set; } = true;

        public IRuntimeRequirementChecker Wrap(IRuntimeRequirementChecker inner) => new Checker(this, inner);

        private sealed class Checker(SwitchableChecker owner, IRuntimeRequirementChecker inner) : IRuntimeRequirementChecker
        {
            public RuntimeRequirementCheckResult Check(RuntimeRequirementCheckSubject subject)
            {
                var result = inner.Check(subject);
                if (owner.MemberSelectionAvailable)
                    return result;
                return result with
                {
                    Requirements = result.Requirements.Select(entry => entry.ConsumerKey == RuntimeReadSelection.RequirementKey
                        ? entry with { Status = RuntimeRequirementStatus.Missing, SupportedSchemaVersions = [] }
                        : entry).ToArray()
                };
            }
        }
    }
}
