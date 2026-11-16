using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Validators;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed class ValueFlowValidatorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" \t ", false)]
    [InlineData(" start ", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" \t ", true)]
    [InlineData(" start ", true)]
    public async Task An_omitted_flowchart_start_accepts_ordered_writes_but_rejects_a_real_fork(string? start, bool fork)
    {
        var flow = Flowchart("flow", [Activity("start"), Set("write-left", "counter", "workflow"), Set("write-right", "counter", "workflow")],
            fork ? [("left", "start", "write-left"), ("right", "start", "write-right")]
                : [("left", "start", "write-left"), ("right", "write-left", "write-right")], start);
        var errors = await Validate(Sequence("root", flow));
        Assert.Equal(fork, errors.Any(error => error.Type == "ValueFlow/ConcurrentWrite"));
        if (fork)
            Assert.Equal("$workflow/variables/counter", Assert.Single(errors, error => error.Type == "ValueFlow/ConcurrentWrite").Path);
    }

    [Fact]
    public async Task The_preserved_single_argument_constructor_refuses_validation_without_shared_authority()
    {
        using var provider = StructureProvider();
        var validator = new ValueFlowValidator(provider.GetRequiredService<IActivityStructureService>());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.Validate(new StubDraft(new([], Activity("consumer"), [], [], null)), CancellationToken.None).AsTask());
        Assert.Contains("shared activity data selection analyzer", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData(" start ")]
    public async Task Required_result_reads_accept_the_same_normalized_flowchart_entry_as_runtime(string start)
    {
        var flow = Flowchart("flow", [Activity("start"), Activity("producer"), Activity("consumer", ResultInput("value", "producer", "flow"))],
            [("produce", "start", "producer"), ("consume", "producer", "consumer")], start);
        Assert.DoesNotContain(await Validate(Sequence("root", flow)), error => error.Type == "ValueFlow/UnavailableProducer");
    }

    [Fact]
    public async Task Concurrent_ordinary_writes_to_the_same_variable_are_rejected()
    {
        var parallel = Parallel(
            ("left", Set("set-left", "counter", "$workflow")),
            ("right", Set("set-right", "counter", "$workflow")));

        var errors = await Validate(Sequence("root", parallel));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
        Assert.Equal("$workflow/variables/counter", error.Path);
    }

    [Fact]
    public async Task Explicit_merge_after_parallel_join_is_accepted()
    {
        var parallel = Parallel(
            ("left", Activity("left")),
            ("right", Activity("right")));

        var errors = await Validate(Sequence("root", parallel, Merge("merge", "counter", "workflow")));

        Assert.DoesNotContain(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
    }

    [Fact]
    public async Task Merge_nodes_inside_parallel_branches_do_not_make_last_writer_wins_deterministic()
    {
        var parallel = Parallel(
            ("left", Merge("merge-left", "counter", "workflow")),
            ("right", Merge("merge-right", "counter", "workflow")));

        var errors = await Validate(Sequence("root", parallel));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
        Assert.Equal("$workflow/variables/counter", error.Path);
    }

    [Fact]
    public async Task Required_result_from_a_later_sequence_node_is_rejected_as_unavailable()
    {
        var consumer = Activity("consumer", ResultInput("value", "producer", "$workflow"));
        var producer = Activity("producer");

        var errors = await Validate(Sequence("root", consumer, producer));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/UnavailableProducer");
        Assert.Equal("consumer/inputs/value", error.Path);
    }

    [Fact]
    public async Task Required_result_that_does_not_dominate_a_flowchart_consumer_is_rejected()
    {
        var start = Activity("start");
        var producer = Activity("producer");
        var bypass = Activity("bypass");
        var consumer = Activity("consumer", ResultInput("value", "producer", "flow"));
        var flowchart = Flowchart(
            "flow",
            [start, producer, bypass, consumer],
            [
                ("produce", "start", "producer"),
                ("skip", "start", "bypass"),
                ("produced", "producer", "consumer"),
                ("bypassed", "bypass", "consumer")
            ],
            "start");

        var errors = await Validate(Sequence("root", flowchart));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/UnavailableProducer");
        Assert.Equal("consumer/inputs/value", error.Path);
    }

    [Fact]
    public async Task Optional_result_from_a_real_conditional_producer_keeps_its_existing_absence_semantics()
    {
        var flowchart = Flowchart("flow", [Activity("start"), Activity("producer"), Activity("bypass"),
                Activity("consumer", ResultInput("value", "producer", "scope:root", isOptional: true))],
            [("produce", "start", "producer"), ("skip", "start", "bypass"),
                ("produced", "producer", "consumer"), ("bypassed", "bypass", "consumer")], "start");
        Assert.Empty(await Validate(Sequence("root", flowchart)));
    }

    [Fact]
    public async Task Direct_result_read_across_a_structured_scope_boundary_is_rejected()
    {
        var producer = Activity("inner-producer");
        var innerScope = Sequence("inner-scope", producer, Return("return", "inner-producer", "inner-scope"));
        var consumer = Activity("consumer", ResultInput("value", "inner-producer", "inner-scope"));

        var errors = await Validate(Sequence("root", innerScope, consumer));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/ScopeBoundary");
        Assert.Equal("consumer/inputs/value", error.Path);
    }

    [Fact]
    public async Task Structured_result_with_an_explicit_return_is_available_to_a_downstream_consumer()
    {
        var producer = Activity("inner-producer");
        var innerScope = Sequence("inner-scope", producer, Return("return", "inner-producer", "inner-scope"));
        var consumer = Activity("consumer", ResultInput("value", "inner-scope", "$workflow"));

        var errors = await Validate(Sequence("root", innerScope, consumer));

        Assert.DoesNotContain(errors, item => item.Type.StartsWith("ValueFlow/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Result_lookup_over_a_cyclic_back_edge_is_rejected()
    {
        var first = Activity("first", ResultInput("previous", "second", "flow"));
        var second = Activity("second");
        var flowchart = Flowchart(
            "flow",
            [first, second],
            [("forward", "first", "second"), ("back", "second", "first")],
            "first");

        var errors = await Validate(Sequence("root", flowchart));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/CyclicBackEdge");
        Assert.Equal("first/inputs/previous", error.Path);
    }

    [Fact]
    public async Task Parallel_branches_require_unique_stable_identities()
    {
        var parallel = Parallel(
            ("same", Activity("left")),
            ("same", Activity("right")));

        var errors = await Validate(Sequence("root", parallel));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/UnstableCollectionIdentity");
        Assert.Equal("parallel/structure/branches/same", error.Path);
    }

    // Spec 238: a Flowchart run begins at the trigger that fired or at the start node, so every child without an
    // inbound connection is an entry point alongside the start node.

    [Fact]
    public async Task Trigger_branch_consumer_reading_its_own_trigger_result_is_accepted()
    {
        var flowchart = Flowchart(
            "flow",
            [Activity("start"), Activity("start-next"), Activity("trigger"), Activity("consumer", ResultInput("value", "trigger", "flow"))],
            [("start-path", "start", "start-next"), ("trigger-path", "trigger", "consumer")],
            "start");

        var errors = await Validate(Sequence("root", flowchart));

        Assert.DoesNotContain(errors, item => item.Type.StartsWith("ValueFlow/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trigger_branch_consumer_also_reached_from_the_start_node_is_rejected()
    {
        var flowchart = Flowchart(
            "flow",
            [Activity("start"), Activity("trigger"), Activity("consumer", ResultInput("value", "trigger", "flow"))],
            [("start-path", "start", "consumer"), ("trigger-path", "trigger", "consumer")],
            "start");

        var errors = await Validate(Sequence("root", flowchart));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/UnavailableProducer");
        Assert.Equal("consumer/inputs/value", error.Path);
    }

    [Fact]
    public async Task Writes_on_different_trigger_branches_are_not_concurrent()
    {
        var flowchart = Flowchart(
            "flow",
            [Activity("start"), Set("set-start", "counter", "$workflow"), Activity("trigger"), Set("set-trigger", "counter", "$workflow")],
            [("start-path", "start", "set-start"), ("trigger-path", "trigger", "set-trigger")],
            "start");

        var errors = await Validate(Sequence("root", flowchart));

        Assert.DoesNotContain(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
    }

    [Fact]
    public async Task Writes_on_a_fork_from_one_entry_are_still_concurrent()
    {
        var flowchart = Flowchart(
            "flow",
            [Activity("start"), Set("set-left", "counter", "$workflow"), Set("set-right", "counter", "$workflow")],
            [("left", "start", "set-left"), ("right", "start", "set-right")],
            "start");

        var errors = await Validate(Sequence("root", flowchart));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
        Assert.Equal("$workflow/variables/counter", error.Path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_legacy_structure_replacement_with_unknown_fork_facts_refuses_only_colliding_writes(bool sameVariable)
    {
        using var actual = StructureProvider();
        var legacy = new LegacyStructureService(actual.GetRequiredService<IActivityStructureService>());
        var flowchart = Flowchart("flow",
            [Activity("start"), Set("write-left", "counter", "workflow"), Set("write-right", sameVariable ? "counter" : "other", "workflow")],
            [("left", "start", "write-left"), ("right", "start", "write-right")], "start");

        var errors = await Validate(flowchart, legacy);

        Assert.Equal(sameVariable, errors.Any(error => error.Type == "ValueFlow/ConcurrentWrite"));
        if (sameVariable)
            Assert.Equal("$workflow/variables/counter", Assert.Single(errors, error => error.Type == "ValueFlow/ConcurrentWrite").Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_legacy_completed_before_declaration_proves_sequential_writes_safe_in_either_projection_order(bool reverseProjection)
    {
        using var actual = StructureProvider();
        var legacy = new LegacyStructureService(actual.GetRequiredService<IActivityStructureService>(), ordered: true);
        var left = Set("write-left", "counter", "workflow");
        var right = Set("write-right", "counter", "workflow");
        var flowchart = Flowchart("flow",
            reverseProjection ? [Activity("start"), right, left] : [Activity("start"), left, right],
            [("left", "start", "write-left"), ("right", "write-left", "write-right")], "start");

        Assert.DoesNotContain(await Validate(flowchart, legacy), error => error.Type == "ValueFlow/ConcurrentWrite");
    }

    [Fact]
    public async Task Mid_flow_node_with_an_inbound_connection_is_not_an_entry_point()
    {
        var flowchart = Flowchart(
            "flow",
            [Activity("start"), Activity("producer"), Activity("mid-flow-trigger"), Activity("consumer", ResultInput("value", "producer", "flow"))],
            [("produce", "start", "producer"), ("wait", "producer", "mid-flow-trigger"), ("resume", "mid-flow-trigger", "consumer")],
            "start");

        var errors = await Validate(Sequence("root", flowchart));

        Assert.DoesNotContain(errors, item => item.Type.StartsWith("ValueFlow/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cyclic_back_edge_lookup_on_a_trigger_branch_is_rejected()
    {
        var flowchart = Flowchart(
            "flow",
            [Activity("start"), Activity("trigger"), Activity("first", ResultInput("previous", "second", "flow")), Activity("second")],
            [("enter", "trigger", "first"), ("forward", "first", "second"), ("back", "second", "first")],
            "start");

        var errors = await Validate(Sequence("root", flowchart));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/CyclicBackEdge");
        Assert.Equal("first/inputs/previous", error.Path);
    }

    // Single-entry regressions (FR-012). Rejected dominance and the single-entry cyclic back-edge are covered by
    // Required_result_that_does_not_dominate_a_flowchart_consumer_is_rejected and
    // Result_lookup_over_a_cyclic_back_edge_is_rejected; the single-entry fork by
    // Writes_on_a_fork_from_one_entry_are_still_concurrent.

    [Fact]
    public async Task Required_result_that_dominates_a_flowchart_consumer_is_accepted()
    {
        var flowchart = Flowchart(
            "flow",
            [Activity("start"), Activity("producer"), Activity("consumer", ResultInput("value", "producer", "flow"))],
            [("produce", "start", "producer"), ("consume", "producer", "consumer")],
            "start");

        var errors = await Validate(Sequence("root", flowchart));

        Assert.DoesNotContain(errors, item => item.Type.StartsWith("ValueFlow/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Switch_predicates_keep_their_projected_input_location_and_sequence_order(bool producerFirst)
    {
        var producer = Activity("flag");
        var condition = ResultInput("ignored", "flag", "root");
        var switchNode = new ActivityNode("switch", "switch@1",
            [ResultInput("Value", "unused-value-producer", "root")], [],
            new ActivityNodeStructure(Elsa.Activities.Switch.Activities.Switch.StructureKind,
                Elsa.Activities.Switch.Activities.Switch.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    cases = new[] { new { caseId = "case-a", match = "Approved", condition, activity = Activity("branch") } },
                    @default = (ActivityNode?)null
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var errors = await Validate(producerFirst ? Sequence("root", producer, switchNode) : Sequence("root", switchNode, producer));

        Assert.DoesNotContain(errors, error => error.Path == "switch/inputs/Value");
        if (producerFirst)
            Assert.DoesNotContain(errors, error => error.Type.StartsWith("ValueFlow/", StringComparison.Ordinal));
        else
            Assert.Equal("switch/inputs/" + Elsa.Activities.Switch.Activities.Switch.ConditionInputKey("case-a"),
                Assert.Single(errors, error => error.Type == "ValueFlow/UnavailableProducer").Path);
    }

    private static async Task<IReadOnlyCollection<Elsa.Workflows.Design.Validations.Core.Models.ValidationError>> Validate(ActivityNode root, IActivityStructureService? structures = null)
    {
        using var services = StructureProvider(structures);
        var validator = new ValueFlowValidator(services.GetRequiredService<IActivityStructureService>(), services.GetRequiredService<IActivityDataSelectionAnalyzer>());
        var draft = new StubDraft(new WorkflowDefinitionState([], root, [], [], null));
        return [.. await validator.Validate(draft, CancellationToken.None)];
    }

    private static ServiceProvider StructureProvider(IActivityStructureService? structures = null)
    {
        var catalog = new StubActivityCatalog().Add("test.activity@1").Add("elsa.sequence.structure@1")
            .Add("elsa.parallel.structure@1").Add("elsa.flowchart.structure@1").Add("switch@1");
        return ActivityDataSelectionTestSupport.Provider(catalog, structures);
    }

    // Models a host implementation compiled against the legacy completion-order port. It delegates
    // child projection to the real owners and intentionally inherits the new unknown-facts default.
    private sealed class LegacyStructureService(IActivityStructureService inner, bool ordered = false) : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => inner.ProjectChildren(activity);
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> children) => inner.ReplaceChildren(activity, children);
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => inner.CompileExecutableStructure(activity);
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => inner.ProjectScopedVariables(activity);
        public bool SupportsScopedVariables(ActivityNode activity) => inner.SupportsScopedVariables(activity);
        public IReadOnlyList<string>? ChildrenCompletedBefore(ActivityNode activity, string childNodeId) =>
            ordered && activity.NodeId == "flow" && childNodeId == "write-right" ? ["write-left"] : null;
    }

    private static ActivityNode Activity(string nodeId, params ArgumentState[] inputs) =>
        new(nodeId, "test.activity@1", inputs, []);

    private static ActivityNode Set(string nodeId, string variableKey, string declaringScopeId) =>
        new ActivityNode(
            nodeId,
            "$intrinsic",
            [
                new ArgumentState("value", new ArgumentValue(1, "Literal"), null, null, null, null)
            ],
            [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(
                AuthoredWorkflowIntrinsicKind.Set,
                new TypeReference("Int32"),
                new VariableReference(variableKey, declaringScopeId))
        };

    private static ActivityNode Merge(string nodeId, string variableKey, string declaringScopeId) =>
        new ActivityNode(
            nodeId,
            "$intrinsic",
            [new ArgumentState("value", new ArgumentValue(1, "Literal"), null, null, null, null)],
            [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(
                AuthoredWorkflowIntrinsicKind.Merge,
                new TypeReference("Int32"),
                new VariableReference(variableKey, declaringScopeId))
        };

    private static ActivityNode Return(string nodeId, string producerNodeId, string producerScopeId) =>
        new ActivityNode(nodeId, "$intrinsic", [ResultInput("value", producerNodeId, producerScopeId)], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Return, new TypeReference("Int32"))
        };

    private static ArgumentState ResultInput(string inputKey, string producerNodeId, string producerScopeId, bool isOptional = false) =>
        new(
            inputKey,
            new ArgumentValue(
                JsonSerializer.SerializeToElement(new { producerNodeId, projectionKey = "$result", producerScopeId, isOptional }),
                "ActivityResult"),
            null,
            null,
            null,
            null);

    private static ActivityNode Sequence(string nodeId, params ActivityNode[] activities) =>
        Structured(nodeId, "elsa.sequence.structure", new { activities, variables = Array.Empty<object>() });

    private static ActivityNode Parallel(params (string Name, ActivityNode Activity)[] branches) =>
        Structured(
            "parallel",
            "elsa.parallel.structure",
            new { branches = branches.Select(branch => new { name = branch.Name, activity = branch.Activity }).ToArray(), threshold = (int?)null });

    private static ActivityNode Flowchart(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities,
        IReadOnlyCollection<(string Id, string Source, string Target)> connections,
        string startNodeId) =>
        Structured(
            nodeId,
            "elsa.flowchart.structure",
            new
            {
                activities,
                connections = connections.Select(connection => new
                {
                    id = connection.Id,
                    source = new { nodeId = connection.Source, port = (string?)null },
                    target = new { nodeId = connection.Target, port = (string?)null }
                }).ToArray(),
                startNodeId,
                nodeMetadata = new Dictionary<string, object>(),
                connectionMetadata = new Dictionary<string, object>(),
                variables = Array.Empty<object>()
            });

    private static ActivityNode Structured(string nodeId, string kind, object payload) =>
        new(
            nodeId,
            $"{kind}@1",
            [],
            [],
            new ActivityNodeStructure(kind, "1.0.0", JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private sealed class StubDraft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft";
        public string WorkflowDefinitionId => "workflow";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
