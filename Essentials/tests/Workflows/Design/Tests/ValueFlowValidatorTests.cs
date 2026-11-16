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
    public async Task An_omitted_flowchart_start_accepts_ordered_writes_but_warns_about_a_real_fork(string? start, bool fork)
    {
        var flow = Flowchart("flow", [Activity("start"), Set("write-left", "counter", "workflow"), Set("write-right", "counter", "workflow")],
            fork ? [("left", "start", "write-left"), ("right", "start", "write-right")]
                : [("left", "start", "write-left"), ("right", "write-left", "write-right")], start);
        var errors = await Validate(Sequence("root", flow));
        Assert.Equal(fork, errors.Any(error => error.Type == "ValueFlow/ConcurrentWrite"));
        if (fork)
            Assert.Equal("$workflow/variables/counter", SingleConcurrentWrite(errors).Path);
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
    public async Task Concurrent_ordinary_writes_to_the_same_variable_are_warned_about()
    {
        var parallel = Parallel(
            ("left", Set("set-left", "counter", "$workflow")),
            ("right", Set("set-right", "counter", "$workflow")));

        var errors = await Validate(Sequence("root", parallel));

        var error = SingleConcurrentWrite(errors);
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

        var error = SingleConcurrentWrite(errors);
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

        var error = SingleConcurrentWrite(errors);
        Assert.Equal("$workflow/variables/counter", error.Path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_legacy_structure_replacement_with_unknown_fork_facts_warns_only_about_colliding_writes(bool sameVariable)
    {
        using var actual = StructureProvider();
        var legacy = new LegacyStructureService(actual.GetRequiredService<IActivityStructureService>());
        var flowchart = Flowchart("flow",
            [Activity("start"), Set("write-left", "counter", "workflow"), Set("write-right", sameVariable ? "counter" : "other", "workflow")],
            [("left", "start", "write-left"), ("right", "start", "write-right")], "start");

        var errors = await Validate(flowchart, legacy);

        Assert.Equal(sameVariable, errors.Any(error => error.Type == "ValueFlow/ConcurrentWrite"));
        if (sameVariable)
            Assert.Equal("$workflow/variables/counter", SingleConcurrentWrite(errors).Path);
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

    // Spec 266, Story 1 scenarios 1 and 2; ADR 0048 required evidence "shared-ancestor parallel capture warning".
    [Fact]
    public async Task Parallel_branches_capturing_into_one_variable_warn_and_name_both_writers()
    {
        var errors = await Validate(Sequence("root", Parallel(
            ("left", Capture("capture-left", "total")),
            ("right", Capture("capture-right", "total")))));

        var warning = SingleConcurrentWrite(errors);
        Assert.Equal("$workflow/variables/total", warning.Path);
        Assert.Contains("'capture-left', 'capture-right'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("last successfully committed write wins", warning.Message, StringComparison.Ordinal);
        Assert.Contains("order the branches, capture into separate variables, or combine them with one write after the join", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Fault lost-update mode", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(errors, error => error.Severity == "Error" && error.Type.StartsWith("ValueFlow/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Separate_variables_combined_after_the_join_do_not_warn()
    {
        var errors = await Validate(Sequence("root",
            Parallel(("left", Capture("capture-left", "left-total")), ("right", Capture("capture-right", "right-total"))),
            Set("combine", "total", "workflow")));

        Assert.DoesNotContain(errors, error => error.Type == "ValueFlow/ConcurrentWrite");
    }

    // Story 1 scenario 3.
    [Fact]
    public async Task A_flowchart_fork_through_one_port_with_two_captures_warns()
    {
        var flowchart = Flowchart("flow",
            [Activity("start"), Capture("capture-left", "total"), Capture("capture-right", "total")],
            [("left", "start", "capture-left"), ("right", "start", "capture-right")], "start");

        Assert.Contains("'capture-left', 'capture-right'", SingleConcurrentWrite(await Validate(Sequence("root", flowchart))).Message, StringComparison.Ordinal);
    }

    // Story 1 scenario 4.
    [Theory]
    [InlineData(AuthoredWorkflowIntrinsicKind.Set)]
    [InlineData(AuthoredWorkflowIntrinsicKind.Merge)]
    [InlineData(AuthoredWorkflowIntrinsicKind.Reduce)]
    public async Task A_capture_and_an_unordered_intrinsic_write_warn(AuthoredWorkflowIntrinsicKind kind)
    {
        var errors = await Validate(Sequence("root", Parallel(
            ("left", Capture("capture", "total")),
            ("right", Intrinsic(kind, "write", "total", "workflow")))));

        Assert.Contains("'capture', 'write'", SingleConcurrentWrite(errors).Message, StringComparison.Ordinal);
    }

    // Story 1 scenario 5.
    [Fact]
    public async Task Three_unordered_writers_give_one_warning_naming_all_three()
    {
        var errors = await Validate(Sequence("root", Parallel(
            ("a", Capture("write-a", "total")),
            ("b", Set("write-b", "total", "workflow")),
            ("c", Capture("write-c", "total")))));

        Assert.Contains("3 nodes", SingleConcurrentWrite(errors).Message, StringComparison.Ordinal);
        Assert.Contains("'write-a', 'write-b', 'write-c'", SingleConcurrentWrite(errors).Message, StringComparison.Ordinal);
    }

    // Story 1 scenario 7 and Story 2 scenario 4.
    [Fact]
    public async Task Consecutive_writes_in_a_sequence_or_on_a_flowchart_path_do_not_warn()
    {
        var flowchart = Flowchart("flow",
            [Capture("path-first", "result"), Capture("path-second", "result")],
            [("next", "path-first", "path-second")], "path-first");

        var errors = await Validate(Sequence("root", Capture("first", "total"), Capture("second", "total"), flowchart));

        Assert.DoesNotContain(errors, error => error.Type == "ValueFlow/ConcurrentWrite");
    }

    // Story 1 scenario 8: a threshold Parallel releases its successor while the other branch still runs.
    [Fact]
    public async Task A_threshold_parallel_leaves_the_outstanding_branch_unordered_with_the_continuation()
    {
        var parallel = Structured("parallel", "elsa.parallel.structure", new
        {
            branches = new[]
            {
                new { name = "a", activity = Activity("branch-a") },
                new { name = "b", activity = Capture("branch-b", "total") }
            },
            threshold = (int?)1
        });

        var errors = await Validate(Sequence("root", parallel, Capture("continuation", "total")));

        Assert.Contains("'branch-b', 'continuation'", SingleConcurrentWrite(errors).Message, StringComparison.Ordinal);
    }

    // Story 1 scenario 8: a merge join runs its continuation on the first arrival.
    [Fact]
    public async Task A_merge_join_does_not_order_an_incoming_branch_before_the_continuation()
    {
        var errors = await Validate(Sequence("root", JoinFlowchart("merge")));

        Assert.Contains("'branch-b', 'continuation'", SingleConcurrentWrite(errors).Message, StringComparison.Ordinal);
    }

    // Story 2 scenario 6: a waiting join orders every incoming branch before its continuation.
    [Fact]
    public async Task A_waiting_join_orders_an_incoming_branch_before_the_continuation()
    {
        Assert.DoesNotContain(await Validate(Sequence("root", JoinFlowchart(null))), error => error.Type == "ValueFlow/ConcurrentWrite");
    }

    [Fact]
    public async Task A_waiting_join_still_reports_unordered_writes_between_incoming_branches()
    {
        var errors = await Validate(Sequence("root", JoinFlowchart(null, branchAWrites: true)));

        Assert.Contains("'branch-a', 'branch-b'", SingleConcurrentWrite(errors).Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'continuation'", SingleConcurrentWrite(errors).Message, StringComparison.Ordinal);
    }

    // Story 2 scenarios 1-3.
    [Fact]
    public async Task If_arms_switch_cases_and_loop_passes_do_not_warn()
    {
        var @if = Structured("if", "elsa.if.structure", new { then = Capture("then", "result"), @else = Capture("else", "result") });
        var @switch = new ActivityNode("switch", "switch@1", [], [],
            new ActivityNodeStructure(Elsa.Activities.Switch.Activities.Switch.StructureKind,
                Elsa.Activities.Switch.Activities.Switch.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    cases = new[]
                    {
                        new { caseId = "case-a", match = "A", condition = (ArgumentState?)null, activity = Capture("case-a", "choice") },
                        new { caseId = "case-b", match = "B", condition = (ArgumentState?)null, activity = Capture("case-b", "choice") }
                    },
                    @default = Capture("default", "choice")
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var loop = Structured("loop", "elsa.while.structure", new { body = Capture("body", "counter") });

        var errors = await Validate(Sequence("root", @if, @switch, loop));

        Assert.DoesNotContain(errors, error => error.Type == "ValueFlow/ConcurrentWrite");
    }

    // Story 2 scenario 5 under ADR 0045 §6 (amendment of 2026-11-16): any activity may select several outcomes, so the
    // paths leaving two ports of a decision node are unordered (D-2a), as before this spec.
    [Fact]
    public async Task Paths_leaving_two_ports_of_a_node_are_unordered_because_any_node_may_select_several_outcomes()
    {
        var flowchart = Structured("flow", "elsa.flowchart.structure", new
        {
            activities = new[] { Activity("decision"), Capture("on-yes", "result"), Capture("on-no", "result") },
            connections = new[]
            {
                new { id = "yes", source = new { nodeId = "decision", port = "Yes" }, target = new { nodeId = "on-yes", port = (string?)null } },
                new { id = "no", source = new { nodeId = "decision", port = "No" }, target = new { nodeId = "on-no", port = (string?)null } }
            },
            startNodeId = "decision",
            nodeMetadata = new Dictionary<string, object>(),
            connectionMetadata = new Dictionary<string, object>(),
            variables = Array.Empty<object>()
        });

        Assert.Contains("'on-yes', 'on-no'", SingleConcurrentWrite(await Validate(Sequence("root", flowchart))).Message, StringComparison.Ordinal);
    }

    // Story 4 scenario 3 and SC-004: a structure registered only here takes part through its declaration alone.
    [Theory]
    [InlineData(null, true)]
    [InlineData(ActivityChildTimeRelation.Unordered, false)]
    [InlineData(ActivityChildTimeRelation.MutuallyExclusive, false)]
    public async Task A_test_only_structure_takes_part_through_its_declaration(ActivityChildTimeRelation? declared, bool undeclared)
    {
        using var actual = StructureProvider();
        var custom = new CustomStructureService(actual.GetRequiredService<IActivityStructureService>(), declared);
        var node = new ActivityNode("custom", "test.activity@1", [], [],
            new ActivityNodeStructure("test.custom.structure", "1", JsonSerializer.SerializeToElement(new { })));

        var errors = await Validate(Sequence("root", node), custom);

        if (declared == ActivityChildTimeRelation.MutuallyExclusive)
        {
            Assert.DoesNotContain(errors, error => error.Type == "ValueFlow/ConcurrentWrite");
            return;
        }

        var warning = SingleConcurrentWrite(errors);
        Assert.Contains("'custom-left', 'custom-right'", warning.Message, StringComparison.Ordinal);
        Assert.Equal(undeclared, warning.Message.Contains("Structure 'test.custom.structure' (node 'custom') does not declare how its children are ordered", StringComparison.Ordinal));
    }

    // Edge case: two outputs of one node capture into one variable (FR-010).
    [Fact]
    public async Task Two_outputs_of_one_node_capturing_into_one_variable_get_a_duplicate_capture_warning()
    {
        var node = new ActivityNode("node", "test.activity@1", [], [VariableOutput("first", "total"), VariableOutput("second", "total")]);

        var errors = await Validate(Sequence("root", node));

        var warning = Assert.Single(errors, error => error.Type == "ValueFlow/DuplicateCaptureTarget");
        Assert.Equal("Warning", warning.Severity);
        Assert.Equal("node/outputs/second", warning.Path);
        Assert.DoesNotContain(errors, error => error.Type == "ValueFlow/ConcurrentWrite");
    }

    private static Elsa.Workflows.Design.Validations.Core.Models.ValidationError SingleConcurrentWrite(
        IEnumerable<Elsa.Workflows.Design.Validations.Core.Models.ValidationError> errors)
    {
        var warning = Assert.Single(errors, error => error.Type == "ValueFlow/ConcurrentWrite");
        Assert.Equal("Warning", warning.Severity);
        return warning;
    }

    // fork -> a, b; a -> join, b -> join; join -> continuation. Branch b and the continuation write total.
    private static ActivityNode JoinFlowchart(string? joinPolicy, bool branchAWrites = false) =>
        Structured("flow", "elsa.flowchart.structure", new
        {
            activities = new[]
            {
                Activity("fork"),
                branchAWrites ? Capture("branch-a", "total") : Activity("branch-a"),
                Capture("branch-b", "total"),
                Activity("join"),
                Capture("continuation", "total")
            },
            connections = new[]
            {
                Connection("to-a", "fork", "branch-a"), Connection("to-b", "fork", "branch-b"),
                Connection("a-join", "branch-a", "join"), Connection("b-join", "branch-b", "join"),
                Connection("join-next", "join", "continuation")
            },
            startNodeId = "fork",
            nodeMetadata = joinPolicy is null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object> { ["join"] = new { policyKind = joinPolicy } },
            connectionMetadata = new Dictionary<string, object>(),
            variables = Array.Empty<object>()
        });

    private static object Connection(string id, string source, string target) =>
        new { id, source = new { nodeId = source, port = (string?)null }, target = new { nodeId = target, port = (string?)null } };

    private static ActivityNode Capture(string nodeId, string variableKey) =>
        new(nodeId, "test.activity@1", [], [VariableOutput("result", variableKey)]);

    private static ArgumentState VariableOutput(string outputKey, string variableKey) =>
        new(outputKey,
            new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = variableKey, declaringScopeId = "workflow" }), "Variable"),
            null, null, null, null);

    private static ActivityNode Intrinsic(AuthoredWorkflowIntrinsicKind kind, string nodeId, string variableKey, string declaringScopeId) =>
        new ActivityNode(nodeId, "$intrinsic", [new ArgumentState("value", new ArgumentValue(1, "Literal"), null, null, null, null)], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(kind, new TypeReference("Int32"), new VariableReference(variableKey, declaringScopeId))
        };

    // A structure kind no module owns: it projects two capturing children and declares only what the test says.
    private sealed class CustomStructureService(IActivityStructureService inner, ActivityChildTimeRelation? declared) : IActivityStructureService
    {
        private static readonly ActivityNode[] Children = [Capture("custom-left", "total"), Capture("custom-right", "total")];

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            activity.Structure?.Kind == "test.custom.structure" ? [new ActivityChildProjection("children", Children)] : inner.ProjectChildren(activity);

        public ActivityChildTimeRelation RelateChildren(ActivityNode activity, string first, string second) =>
            activity.Structure?.Kind == "test.custom.structure"
                ? first == second ? ActivityChildTimeRelation.Unspecified : declared ?? ActivityChildTimeRelation.Unspecified
                : inner.RelateChildren(activity, first, second);

        public bool? CompletesAfterItsChildren(ActivityNode activity) =>
            activity.Structure?.Kind == "test.custom.structure" ? true : inner.CompletesAfterItsChildren(activity);

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> children) => inner.ReplaceChildren(activity, children);
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => inner.CompileExecutableStructure(activity);
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => inner.ProjectScopedVariables(activity);
        public bool SupportsScopedVariables(ActivityNode activity) => inner.SupportsScopedVariables(activity);
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
            .Add("elsa.parallel.structure@1").Add("elsa.flowchart.structure@1").Add("switch@1")
            .Add("elsa.if.structure@1").Add("elsa.while.structure@1");
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
