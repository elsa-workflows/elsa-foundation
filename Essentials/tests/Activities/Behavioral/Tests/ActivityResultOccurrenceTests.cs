using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
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
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DoActivity = Elsa.Activities.Do.Activities.Do;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// Which occurrence of a producer a reader gets when it binds an input to an activity result, run through the real
/// engine. A reader binds in one of three forms: directly with a declared producer scope (existing documents), directly
/// without one (what the server returns since #4053), or through an expression parameter, which has no scope. Every
/// producer here returns its own activity execution id, so each occurrence has a distinct result and the value a
/// reader committed as its input names the occurrence it read.
/// </summary>
/// <remarks>
/// These tests pin the engine's behaviour; #4053 changed no resolver rule. The ones marked "known defect" describe a
/// wrong or missing value that stays reachable for scoped bindings and expression parameters (#4056). Publication
/// refuses the direct binding without a scope in each of those shapes, so the server never offers it there
/// (spec 1215, <c>research-4053-activity-result-scope.md</c>).
/// </remarks>
public sealed class ActivityResultOccurrenceTests
{
    private const string ResultKey = nameof(ReadResult.Value);

    [Fact]
    public async Task A_loop_reader_that_declares_the_root_scope_reads_the_first_pass_result_on_the_second_pass()
    {
        // Known defect: on the second pass the reader should get that pass's result. The declared root scope hides
        // the second-pass producer, which runs in a generated loop scope, and leaves the first-pass one.
        await using var harness = NewHarness("again", "done");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Loop(Direct("node-b", "scope:root"))));

        run.AssertWorkflowCompleted();
        var produced = Occurrences(run, "node-b");
        Assert.Equal(2, produced.Count);
        Assert.Equal([produced[0], produced[0]], Reads(run, "node-c"));
    }

    [Theory]
    [InlineData(Form.Direct)]
    [InlineData(Form.Parameter)]
    public async Task A_loop_reader_without_a_scope_cannot_tell_the_two_passes_apart_on_the_second_pass(Form form)
    {
        // Known defect: the second-pass read does not resolve. Both passes' producers are in the reader's lineage
        // and the first pass has no iteration frame, so nothing separates them. Publication refuses the direct form
        // (ValueFlow/EarlierProducerRun); the parameter keeps today's rules (#4056).
        await using var harness = NewHarness("again", "done");

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Loop(WithoutScope(form, "node-b"))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        var produced = Occurrences(run, "node-b");
        Assert.Equal(2, produced.Count);
        Assert.Equal([produced[0]], Reads(run, "node-c"));
        var blocked = await AssertBlockedAsync(harness, run, "node-c");
        var failure = Assert.Throws<CausalActivityResultResolutionException>(() => ResolveWithoutScope(run, "node-b", blocked));
        Assert.Equal(CausalActivityResultResolutionFailureReason.Ambiguous, failure.Reason);
        Assert.Equal(produced, failure.CandidateInvocationIds);
    }

    [Fact]
    public async Task A_result_produced_under_a_first_wins_fork_is_unavailable_to_a_reader_that_declares_the_root_scope()
    {
        // Known defect for existing documents: the producer ran once, before the reader, in a generated race scope no
        // author can name. The binding the server returns declares no scope and resolves (next test).
        await using var harness = NewHarness();

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Race(Direct("node-p", "scope:root"))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        run.AssertCompleted("node-p");
        var blocked = await AssertBlockedAsync(harness, run, "node-r");
        var failure = Assert.Throws<CausalActivityResultResolutionException>(() => ResolveDirect(run, "node-p", "scope:root", blocked));
        Assert.Equal(CausalActivityResultResolutionFailureReason.Unavailable, failure.Reason);
    }

    [Theory]
    [InlineData(Form.Direct)]
    [InlineData(Form.Parameter)]
    public async Task A_result_produced_under_a_first_wins_fork_resolves_for_a_reader_without_a_scope(Form form)
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Race(WithoutScope(form, "node-p"))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        Assert.Equal(Occurrences(run, "node-p"), Reads(run, "node-r"));
    }

    [Theory]
    [InlineData("node-root")]
    [InlineData("scope:root")]
    public async Task The_result_of_a_placed_reusable_activity_is_unavailable_to_a_sibling_that_declares_a_scope(string declaredScope)
    {
        // Known defect for existing documents: a placed reusable activity's scope is its own activity execution id,
        // which no authored scope can equal. The binding the server returns declares no scope and resolves (next test).
        await using var harness = NewHarness();
        var root = Sequence("node-root", Stamp("node-p", PlacedReusableActivity), Read("node-r", Direct("node-p", declaredScope)));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(root),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        var producer = run.AssertCompleted("node-p");
        Assert.Equal(producer.InvocationId, producer.Provenance.ExecutionScopeId);
        var blocked = await AssertBlockedAsync(harness, run, "node-r");
        var failure = Assert.Throws<CausalActivityResultResolutionException>(() => ResolveDirect(run, "node-p", declaredScope, blocked));
        Assert.Equal(CausalActivityResultResolutionFailureReason.Unavailable, failure.Reason);
    }

    [Theory]
    [InlineData(Form.Direct)]
    [InlineData(Form.Parameter)]
    public async Task The_result_of_a_placed_reusable_activity_resolves_for_a_sibling_without_a_scope(Form form)
    {
        await using var harness = NewHarness();
        var root = Sequence("node-root", Stamp("node-p", PlacedReusableActivity), Read("node-r", WithoutScope(form, "node-p")));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        run.AssertWorkflowCompleted();
        Assert.Equal(Occurrences(run, "node-p"), Reads(run, "node-r"));
    }

    [Theory]
    [InlineData("node-root")]
    [InlineData("scope:root")]
    [InlineData("not a scope of this workflow")]
    public async Task A_sequence_reader_resolves_its_prior_sibling_whatever_scope_it_declares(string declaredScope)
    {
        // A Sequence child has no scope at run time, so the declared one is never compared.
        await using var harness = NewHarness();
        var root = Sequence("node-root", Stamp("node-p"), Read("node-r", Direct("node-p", declaredScope)));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        run.AssertWorkflowCompleted();
        Assert.Null(run.State("node-p").Provenance.ExecutionScopeId);
        Assert.Equal(Occurrences(run, "node-p"), Reads(run, "node-r"));
    }

    [Theory]
    [InlineData(Form.Direct)]
    [InlineData(Form.Parameter)]
    public async Task A_sequence_reader_resolves_its_prior_sibling_without_a_scope(Form form)
    {
        await using var harness = NewHarness();
        var root = Sequence("node-root", Stamp("node-p"), Read("node-r", WithoutScope(form, "node-p")));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        run.AssertWorkflowCompleted();
        Assert.Equal(Occurrences(run, "node-p"), Reads(run, "node-r"));
    }

    [Theory]
    [InlineData(Form.Direct)]
    [InlineData(Form.Parameter)]
    public async Task A_flowchart_reader_resolves_a_prior_node_without_a_scope(Form form)
    {
        // The producer runs in the Flowchart's root scope, which a scope-free reference never has to name.
        await using var harness = NewHarness();
        var root = Flowchart(
            "node-flowchart",
            [Probe("node-a"), Stamp("node-p"), Read("node-r", WithoutScope(form, "node-p"))],
            [Connect("node-a", "node-p"), Connect("node-p", "node-r")],
            startNodeId: "node-a");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        run.AssertWorkflowCompleted();
        var producer = Assert.Single(Completed(run, "node-p"));
        Assert.Equal("scope:root", producer.Provenance.ExecutionScopeId
            ?? producer.Metadata.GetValueOrDefault(RuntimeMetadataKeys.FlowchartExecutionScopeId));
        Assert.Equal(Occurrences(run, "node-p"), Reads(run, "node-r"));
    }

    [Theory]
    [InlineData(Form.Scoped)]
    [InlineData(Form.Direct)]
    [InlineData(Form.Parameter)]
    public async Task A_loop_reader_resolves_a_producer_that_ran_once_before_the_flowchart_on_every_pass(Form form)
    {
        // The producer runs once, outside the loop, so every pass has exactly one occurrence to read.
        await using var harness = NewHarness("again", "done");
        var read = form == Form.Scoped ? Direct("node-p", "node-root") : WithoutScope(form, "node-p");
        var loop = Flowchart(
            "node-flowchart",
            [Probe("node-a"), LoopingRead("node-c", read), Probe("node-z")],
            [Connect("node-a", "node-c"), Connect("node-c", "node-a", "again"), Connect("node-c", "node-z", "done")],
            startNodeId: "node-a");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Sequence("node-root", Stamp("node-p"), loop)));

        run.AssertWorkflowCompleted();
        var produced = Assert.Single(Occurrences(run, "node-p"));
        Assert.Equal([produced, produced], Reads(run, "node-c"));
    }

    [Theory]
    [InlineData(Form.Scoped)]
    [InlineData(Form.Direct)]
    [InlineData(Form.Parameter)]
    public async Task A_reader_in_a_looping_flowchart_nested_in_a_loop_always_reads_the_outer_first_pass_result(Form form)
    {
        // Known defect, in every form: the outer producer runs once per outer pass, and the nested reader should get
        // the current outer pass's result. The nested Flowchart's nodes carry only its own iteration key (none on its
        // first pass), so the second-pass outer producer never matches their frame, and the first-pass one, which has
        // no frame, always does. Publication refuses the direct form without a scope, because the outer Flowchart
        // states that the producer can reach itself (ValueFlow/EarlierProducerRun); the other forms keep today's
        // rules (#4056).
        await using var harness = NewHarness("again", "done", "again", "again", "done", "done");
        var read = form == Form.Scoped ? Direct("node-b", "scope:root") : WithoutScope(form, "node-b");
        var inner = Flowchart(
            "node-inner",
            [Probe("node-d"), LoopingRead("node-e", read)],
            [Connect("node-d", "node-e"), Connect("node-e", "node-d", "again")],
            startNodeId: "node-d");
        var outer = Flowchart(
            "node-flowchart",
            [Probe("node-a"), Stamp("node-b"), inner, LoopingRead("node-g", Literal("outer gate")), Probe("node-z")],
            [
                Connect("node-a", "node-b"), Connect("node-b", "node-inner"), Connect("node-inner", "node-g"),
                Connect("node-g", "node-a", "again"), Connect("node-g", "node-z", "done")
            ],
            startNodeId: "node-a");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(outer));

        run.AssertWorkflowCompleted();
        var produced = Occurrences(run, "node-b");
        Assert.Equal(2, produced.Count);
        Assert.Equal([produced[0], produced[0], produced[0], produced[0]], Reads(run, "node-e"));
    }

    [Theory]
    [InlineData("While", Form.Scoped)]
    [InlineData("While", Form.Direct)]
    [InlineData("While", Form.Parameter)]
    [InlineData("Do", Form.Scoped)]
    [InlineData("Do", Form.Direct)]
    [InlineData("Do", Form.Parameter)]
    [InlineData("For", Form.Scoped)]
    [InlineData("For", Form.Direct)]
    [InlineData("For", Form.Parameter)]
    [InlineData("ForEach", Form.Scoped)]
    [InlineData("ForEach", Form.Direct)]
    [InlineData("ForEach", Form.Parameter)]
    public async Task A_reader_in_a_loop_body_reads_the_producer_of_its_own_pass(string loop, Form form)
    {
        // The loop schedules every pass itself, so an earlier pass's body is never in a later pass's lineage. That is
        // why the loops state that no earlier run of their body leads to a reader.
        await using var harness = NewHarness("Done", "Break");
        var read = form == Form.Scoped ? Direct("node-p", "node-body") : WithoutScope(form, "node-p");
        var endsItself = loop is "While" or "Do";
        var body = Sequence("node-body", Stamp("node-p"), endsItself ? LoopingRead("node-c", read) : Read("node-c", read));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(LoopOver(loop, body)));

        run.AssertWorkflowCompleted();
        var produced = Occurrences(run, "node-p");
        Assert.Equal(2, produced.Count);
        Assert.Equal(produced, Reads(run, "node-c"));
    }

    [Fact]
    public async Task A_reader_beside_its_producer_in_a_sequence_on_a_flowchart_loop_reads_the_first_pass_result_when_it_declares_the_root_scope()
    {
        // Known defect, refused at publication as a cyclic back-edge in every form: the analyzer's ancestor walk asks
        // the Flowchart about the Sequence with itself. The Sequence completes on every pass, and a completed
        // composite leads back to its last child, so the first pass's producer is in the second pass's lineage.
        await using var harness = NewHarness("again", "done");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(SequenceOnLoop(Direct("node-p", "scope:root"))));

        run.AssertWorkflowCompleted();
        var produced = Occurrences(run, "node-p");
        Assert.Equal(2, produced.Count);
        Assert.Equal([produced[0], produced[0]], Reads(run, "node-c"));
    }

    [Theory]
    [InlineData(Form.Direct)]
    [InlineData(Form.Parameter)]
    public async Task A_reader_beside_its_producer_in_a_sequence_on_a_flowchart_loop_cannot_tell_the_passes_apart_without_a_scope(Form form)
    {
        // Known defect, refused at publication as a cyclic back-edge.
        await using var harness = NewHarness("again", "done");

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(SequenceOnLoop(WithoutScope(form, "node-p"))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        var produced = Occurrences(run, "node-p");
        Assert.Equal(2, produced.Count);
        Assert.Equal([produced[0]], Reads(run, "node-c"));
        var blocked = await AssertBlockedAsync(harness, run, "node-c");
        var failure = Assert.Throws<CausalActivityResultResolutionException>(() => ResolveWithoutScope(run, "node-p", blocked));
        Assert.Equal(CausalActivityResultResolutionFailureReason.Ambiguous, failure.Reason);
        Assert.Equal(produced, failure.CandidateInvocationIds);
    }

    [Theory]
    [InlineData(Form.Scoped)]
    [InlineData(Form.Direct)]
    [InlineData(Form.Parameter)]
    public async Task A_reader_after_a_merge_node_reads_the_run_of_that_node_that_scheduled_it(Form form)
    {
        // A merge node runs once per arrival, without a loop. Each run schedules its own successor, so a reader's
        // lineage holds only the run it follows, and reachability to itself is all the Flowchart needs to state.
        await using var harness = NewHarness();
        var read = form == Form.Scoped ? Direct("node-m", "scope:root") : WithoutScope(form, "node-m");
        var root = Flowchart(
            "node-flowchart",
            [Probe("node-fork"), Probe("node-left"), Probe("node-right"), Stamp("node-m"), Read("node-r", read)],
            [
                Connect("node-fork", "node-left"), Connect("node-fork", "node-right"),
                Connect("node-left", "node-m"), Connect("node-right", "node-m"), Connect("node-m", "node-r")
            ],
            startNodeId: "node-fork",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-m"] = new("merge") });

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        run.AssertWorkflowCompleted();
        var produced = Occurrences(run, "node-m");
        Assert.Equal(2, produced.Count);
        Assert.Equal(
            produced.Order(StringComparer.Ordinal),
            Completed(run, "node-r").Select(state => state.SchedulingActivityExecutionId).Order(StringComparer.Ordinal));
        Assert.All(Completed(run, "node-r"), reader =>
            Assert.Equal(reader.SchedulingActivityExecutionId, reader.InputSnapshot!.Values[ResultKey].InlineValue!.Value.GetString()));
    }

    // A -> S -> G, G -(again)-> A, G -(done)-> Z, where S is a Sequence of P then C, and C reads P.
    private static ExecutableNode SequenceOnLoop(RuntimeInputBinding read) => Flowchart(
        "node-flowchart",
        [
            Probe("node-a"), Sequence("node-s", Stamp("node-p"), Read("node-c", read)),
            LoopingRead("node-g", Literal("loop gate")), Probe("node-z")
        ],
        [Connect("node-a", "node-s"), Connect("node-s", "node-g"), Connect("node-g", "node-a", "again"), Connect("node-g", "node-z", "done")],
        startNodeId: "node-a");

    // Two passes of the body: While and Do run until the body breaks, For and ForEach over two items.
    private static ExecutableNode LoopOver(string loop, ExecutableNode body)
    {
        var (type, kind, version, slot, inputs) = loop switch
        {
            "While" => (typeof(WhileActivity), WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion, WhileActivity.BodySlotName,
                new Dictionary<string, object?> { ["Condition"] = true }),
            "Do" => (typeof(DoActivity), DoActivity.StructureKind, DoActivity.StructureSchemaVersion, DoActivity.BodySlotName,
                new Dictionary<string, object?> { ["Condition"] = true }),
            "For" => (typeof(ForActivity), ForActivity.StructureKind, ForActivity.StructureSchemaVersion, ForActivity.BodySlotName,
                new Dictionary<string, object?> { ["Start"] = 0, ["End"] = 2, ["Step"] = 1, ["EndInclusive"] = false }),
            _ => (typeof(ForEachActivity), ForEachActivity.StructureKind, ForEachActivity.StructureSchemaVersion, ForEachActivity.BodySlotName,
                new Dictionary<string, object?> { ["Collection"] = new[] { "first", "second" } })
        };
        return Nodes.Structural("node-loop", type, kind, version, new { body = body.ExecutableNodeId }, [new ExecutableChildSlot(slot, [body])], inputs);
    }

    // A -> B -> C, C -(again)-> A, C -(done)-> Z. B produces, C reads B and decides whether to go round again.
    private static ExecutableNode Loop(RuntimeInputBinding read) => Flowchart(
        "node-flowchart",
        [Probe("node-a"), Stamp("node-b"), LoopingRead("node-c", read), Probe("node-z")],
        [Connect("node-a", "node-b"), Connect("node-b", "node-c"), Connect("node-c", "node-a", "again"), Connect("node-c", "node-z", "done")],
        startNodeId: "node-a");

    // Fork -> P and Fork -> Q under a first-wins race, then P -> R. P produces inside the race; R reads P after it.
    private static ExecutableNode Race(RuntimeInputBinding read) => Flowchart(
        "node-flowchart",
        [Probe("node-fork"), Stamp("node-p"), Probe("node-q"), Read("node-r", read)],
        [Connect("node-fork", "node-p"), Connect("node-fork", "node-q"), Connect("node-p", "node-r")],
        startNodeId: "node-fork",
        nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-fork"] = new("firstWins") });

    /// <summary>Asserts the reader's latest occurrence never started and holds the run open with a blocking incident.</summary>
    private static async Task<ActivityExecutionState> AssertBlockedAsync(WorkflowExecutionHarness harness, WorkflowExecutionRun run, string readerNodeId)
    {
        var blocked = run.States(readerNodeId).OrderBy(state => state.ExecutionSequence).Last();
        Assert.Equal(ActivityExecutionStatus.Scheduled, blocked.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(blocked.InvocationId, incident.ActivityExecutionId);
        Assert.True(incident.IsBlocking);
        return blocked;
    }

    // The incident carries the failure only as text, so the reason is read by resolving the same binding again over
    // the states the run committed.
    private static CausalActivityResultResolution? ResolveDirect(WorkflowExecutionRun run, string producerNodeId, string declaredScope, ActivityExecutionState reader) =>
        new CausalActivityResultResolver().Resolve(new RuntimeActivityResultReference(producerNodeId, ResultKey, declaredScope), reader, run.ActivityStates);

    private static CausalActivityResultResolution? ResolveWithoutScope(WorkflowExecutionRun run, string producerNodeId, ActivityExecutionState reader) =>
        new CausalActivityResultResolver().Resolve(new ActivityResultExpressionParameterBinding(producerNodeId, ResultKey), reader, run.ActivityStates);

    /// <summary>The activity execution ids of a node's completed occurrences, in execution order.</summary>
    private static IReadOnlyList<string> Occurrences(WorkflowExecutionRun run, string nodeId) =>
        Completed(run, nodeId).Select(state => state.InvocationId).ToArray();

    /// <summary>What each completed occurrence of a reader committed as its input, in execution order.</summary>
    private static IReadOnlyList<string?> Reads(WorkflowExecutionRun run, string nodeId) =>
        Completed(run, nodeId).Select(state => state.InputSnapshot!.Values[ResultKey].InlineValue!.Value.GetString()).ToArray();

    private static IEnumerable<ActivityExecutionState> Completed(WorkflowExecutionRun run, string nodeId) =>
        run.States(nodeId).Where(state => state.Status == ActivityExecutionStatus.Completed).OrderBy(state => state.ExecutionSequence);

    private static WorkflowExecutionHarness NewHarness(params string[] loopOutcomes) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithFeature(services => new Expressions.ExpressionsFeature().ConfigureServices(services))
            .WithFeature(services => new Expressions.JavaScript.JavaScriptFeature().ConfigureServices(services))
            .WithFeature(services => new Expressions.JavaScript.Jint.JintFeature().ConfigureServices(services))
            .WithFeature(services => services.AddMemoryCache())
            .WithFeature(services => services.AddSingleton(new LoopOutcomes(loopOutcomes)))
            .WithProbeLeaf()
            .Build(Enumerable.Range(1, 64).Select(index => $"actexec-{index}"));

    // What placement stamps on a reusable activity's boundary node (ActivityTemplatePlacer); the key is what makes
    // the node open its own execution scope.
    private static readonly IReadOnlyDictionary<string, string> PlacedReusableActivity =
        new Dictionary<string, string> { ["activity.definitionVersionId"] = "definition-stamp-version" };

    private static RuntimeInputBinding Direct(string producerNodeId, string declaredScope) => new(
        ResultKey, new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.ActivityResult,
        activityResult: new RuntimeActivityResultReference(producerNodeId, ResultKey, declaredScope));

    /// <summary>How a reader binds to a producer's result.</summary>
    public enum Form
    {
        /// <summary>Directly, declaring the producer's scope, as existing documents do.</summary>
        Scoped,

        /// <summary>Directly, without a scope, as the server returns it.</summary>
        Direct,

        /// <summary>Through an expression parameter, which has no scope.</summary>
        Parameter
    }

    private static RuntimeInputBinding WithoutScope(Form form, string producerNodeId) => form switch
    {
        Form.Direct => new(
            ResultKey, new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference(producerNodeId, ResultKey, producerScopeId: null)),
        Form.Parameter => ThroughParameter(producerNodeId),
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, "A scoped read names its scope.")
    };

    private static RuntimeInputBinding ThroughParameter(string producerNodeId) => new(
        ResultKey, new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Expression,
        expression: new RuntimeExpressionBinding(
            "JavaScript",
            "args.produced",
            parameters: new Dictionary<string, ExpressionParameterBinding>
            {
                ["produced"] = new ActivityResultExpressionParameterBinding(producerNodeId, ResultKey)
            }));

    private static RuntimeInputBinding Literal(string value) => new(
        ResultKey, new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Literal,
        literal: ValueEnvelope.Inline(new ValueTypeDescriptor("Object"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private static ExecutableNode Probe(string nodeId) => WorkflowExecutionHarness.NewProbeNode(nodeId);

    private static ExecutableNode Stamp(string nodeId, IReadOnlyDictionary<string, string>? metadata = null) =>
        Leaf(nodeId, typeof(StampActivity), read: null, metadata);

    private static ExecutableNode Read(string nodeId, RuntimeInputBinding read) => Leaf(nodeId, typeof(ReadActivity), read);

    private static ExecutableNode LoopingRead(string nodeId, RuntimeInputBinding read) => Leaf(nodeId, typeof(LoopingReadActivity), read);

    private static ExecutableNode Leaf(string nodeId, Type activityType, RuntimeInputBinding? read, IReadOnlyDictionary<string, string>? metadata = null) => new(
        executableNodeId: nodeId,
        authoredActivityId: $"authored-{nodeId}",
        activityType: activityType.FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: read is null
            ? new Dictionary<string, RuntimeInputBinding>()
            : new Dictionary<string, RuntimeInputBinding> { [read.InputName] = read },
        metadata: metadata ?? new Dictionary<string, string>());

    private static ExecutableNode Sequence(string nodeId, params ExecutableNode[] steps) =>
        GraphBoundaryCompleteTests.Sequence(nodeId, steps);

    private static FlowchartConnection Connect(string source, string target, string? outcome = null) =>
        new(new FlowchartEndpoint(source, outcome), new FlowchartEndpoint(target));

    private static ExecutableNode Flowchart(
        string nodeId,
        IReadOnlyCollection<ExecutableNode> children,
        IReadOnlyCollection<FlowchartConnection> connections,
        string startNodeId,
        IReadOnlyDictionary<string, FlowchartNodeMetadata>? nodeMetadata = null) => new(
        executableNodeId: nodeId,
        authoredActivityId: $"authored-{nodeId}",
        activityType: typeof(FlowchartActivity).FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding>(),
        metadata: new Dictionary<string, string>(),
        childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, children)],
        structure: new ExecutableActivityStructure(
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new FlowchartStructure(connections, startNodeId, nodeMetadata))));

    public sealed record ReadResult([property: Output(Key = "Value", Path = "value")] object? Value);

    /// <summary>The outcomes the looping readers of one run complete with, in execution order.</summary>
    public sealed class LoopOutcomes(IEnumerable<string> outcomes)
    {
        private readonly Queue<string> _outcomes = new(outcomes);

        public string Next() => _outcomes.Dequeue();
    }

    /// <summary>Returns its own activity execution id, so every occurrence produces a different result.</summary>
    public sealed class StampActivity : Activity<ReadResult>
    {
        protected override ValueTask<ActivityTransition<ReadResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new ReadResult(context.InvocationId)));
    }

    /// <summary>Returns its input, so a test can read which occurrence the binding resolved.</summary>
    public sealed class ReadActivity : Activity<ReadResult>
    {
        [ActivityInput] public object? Value { get; set; }

        protected override ValueTask<ActivityTransition<ReadResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new ReadResult(Value)));
    }

    /// <summary>A reader that also closes a loop: it completes with the run's next scripted outcome.</summary>
    [ActivityOutcome("again")]
    [ActivityOutcome(ActivityOutcomes.Done)]
    [ActivityOutcome(ActivityOutcomes.Break)]
    [ActivityOutcome("done")]
    public sealed class LoopingReadActivity(LoopOutcomes outcomes) : Activity<ReadResult>
    {
        [ActivityInput] public object? Value { get; set; }

        protected override ValueTask<ActivityTransition<ReadResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new ReadResult(Value), outcomes.Next()));
    }
}
