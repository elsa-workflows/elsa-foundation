using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>Spec 266, Story 4: the BPMN process declares how its children relate in time (research R-6).</summary>
public sealed class BpmnChildTimeRelationTests : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;

    public BpmnChildTimeRelationTests()
    {
        var services = new ServiceCollection();
        new Elsa.Activities.Bpmn.Design.ActivitiesBpmnDesignFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>());
    }

    public void Dispose() => _provider.Dispose();

    // Story 4 scenarios 1 and 2.
    [Theory]
    [InlineData(BpmnElementTypes.ParallelGateway, true)]
    [InlineData(BpmnElementTypes.InclusiveGateway, true)]
    [InlineData(BpmnElementTypes.EventBasedGateway, true)]
    [InlineData(BpmnElementTypes.ExclusiveGateway, false)]
    public void Tasks_after_a_split_gateway_warn_unless_the_gateway_is_exclusive(string gatewayType, bool warns)
    {
        var process = Split(gatewayType);

        var findings = Analyze(process).UnorderedWrites;

        Assert.Equal(warns, findings.Count == 1);
        if (warns)
            Assert.Equal(["left", "right"], Assert.Single(findings).WriterNodeIds);
        Assert.Equal(warns ? ActivityChildTimeRelation.Unordered : ActivityChildTimeRelation.MutuallyExclusive,
            _handler.RelateChildren(process, "left", "right"));
    }

    [Fact]
    public void A_task_without_gateways_splitting_its_flow_is_an_implicit_parallel_split()
    {
        var process = Process(
            [Task("a", "fork"), Task("b", "left"), Task("c", "right"), Element("start", BpmnElementTypes.StartEvent)],
            [Flow("start", "a"), Flow("a", "b"), Flow("a", "c")]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "left", "right"));
        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "fork", "right"));
    }

    [Fact]
    public void A_parallel_join_orders_its_branches_before_the_continuation_but_an_exclusive_merge_does_not()
    {
        foreach (var (joinType, ordered) in new[] { (BpmnElementTypes.ParallelGateway, true), (BpmnElementTypes.ExclusiveGateway, false) })
        {
            var process = Process(
                [
                    Element("start", BpmnElementTypes.StartEvent), Element("split", BpmnElementTypes.ParallelGateway),
                    Task("a", "left"), Task("b", "right"), Element("join", joinType), Task("c", "after")
                ],
                [Flow("start", "split"), Flow("split", "a"), Flow("split", "b"), Flow("a", "join"), Flow("b", "join"), Flow("join", "c")]);

            Assert.Equal(ordered ? ActivityChildTimeRelation.FirstBeforeSecond : ActivityChildTimeRelation.Unordered,
                _handler.RelateChildren(process, "right", "after"));
            // An exclusive merge of two parallel tokens runs what follows it twice at once.
            Assert.Equal(ordered ? ActivityChildTimeRelation.FirstBeforeSecond : ActivityChildTimeRelation.Unordered,
                _handler.RelateChildren(process, "after", "after"));
        }
    }

    [Fact]
    public void An_exclusive_merge_after_an_exclusive_split_keeps_its_continuation_ordered()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("split", BpmnElementTypes.ExclusiveGateway),
                Task("a", "left"), Task("b", "right"), Element("join", BpmnElementTypes.ExclusiveGateway), Task("c", "after")
            ],
            [Flow("start", "split"), Flow("split", "a"), Flow("split", "b"), Flow("a", "join"), Flow("b", "join"), Flow("join", "c")]);

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "right", "after"));
        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "after", "after"));
        Assert.Empty(Analyze(process).UnorderedWrites);
    }

    [Theory]
    [InlineData(false, ActivityChildTimeRelation.Unordered)]
    [InlineData(true, ActivityChildTimeRelation.FirstBeforeSecond)]
    public void A_parallel_multi_instance_child_overlaps_itself(bool sequential, ActivityChildTimeRelation expected)
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent),
                new BpmnElement("mi", BpmnElementTypes.Task, childNodeId: "body", loopCharacteristics: new BpmnLoopCharacteristics(sequential, cardinality: 3))
            ],
            [Flow("start", "mi")]);

        Assert.Equal(expected, _handler.RelateChildren(process, "body", "body"));
        Assert.Equal(!sequential, Analyze(process).UnorderedWrites.Count == 1);
    }

    [Theory]
    [InlineData(true, ActivityChildTimeRelation.MutuallyExclusive)]
    [InlineData(false, ActivityChildTimeRelation.Unordered)]
    public void An_interrupting_boundary_path_excludes_the_hosts_normal_continuation(bool interrupting, ActivityChildTimeRelation expected)
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Task("host", "host-task"), Task("next", "normal"),
                new BpmnElement("boundary", BpmnElementTypes.BoundaryEvent, attachedToRef: "host", cancelActivity: interrupting),
                Task("handler", "on-boundary")
            ],
            [Flow("start", "host"), Flow("host", "next"), Flow("boundary", "handler")]);

        Assert.Equal(expected, _handler.RelateChildren(process, "normal", "on-boundary"));
        // An interrupting boundary cancels the host before it can commit; a non-interrupting one runs beside it.
        Assert.Equal(expected, _handler.RelateChildren(process, "host-task", "on-boundary"));
    }

    [Fact]
    public void A_parallel_split_on_a_cycle_makes_its_off_cycle_branch_overlap_itself()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("merge", BpmnElementTypes.ExclusiveGateway),
                Element("split", BpmnElementTypes.ParallelGateway), Task("a", "branch"), Task("l", "loop")
            ],
            [Flow("start", "merge"), Flow("merge", "split"), Flow("split", "a"), Flow("split", "l"), Flow("l", "merge")]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "branch", "branch"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "loop", "branch"));
    }

    // Two tokens can pass an exclusive merge at once, so the choices of a split after it are not exclusive.
    [Fact]
    public void An_exclusive_gateway_passed_by_overlapping_tokens_does_not_make_its_choices_exclusive()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Task("s1", "first"), Task("s2", "second"),
                Element("x", BpmnElementTypes.ExclusiveGateway), Task("ta", "a"), Task("tb", "b")
            ],
            [Flow("start", "s1"), Flow("s1", "s2"), Flow("s1", "x"), Flow("s2", "x"), Flow("x", "ta"), Flow("x", "tb")]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "a", "a"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "a", "b"));
    }

    // Parallel branches inside a loop reach each other only around the cycle; within one pass they run side by side.
    [Fact]
    public void Parallel_branches_inside_a_loop_are_unordered()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("merge", BpmnElementTypes.ExclusiveGateway),
                Element("split", BpmnElementTypes.ParallelGateway), Task("a", "left"), Task("b", "right"),
                Element("join", BpmnElementTypes.ParallelGateway)
            ],
            [Flow("start", "merge"), Flow("merge", "split"), Flow("split", "a"), Flow("split", "b"),
             Flow("a", "join"), Flow("b", "join"), Flow("join", "merge")]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "left", "right"));
    }

    // An exclusive merge closing a loop is reached only around the cycle, so the loop body stays sequential.
    [Fact]
    public void Tasks_in_a_loop_body_closed_by_an_exclusive_merge_stay_ordered()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("xm", BpmnElementTypes.ExclusiveGateway),
                Task("ta", "a"), Task("tb", "b"), Element("xs", BpmnElementTypes.ExclusiveGateway), Element("end", BpmnElementTypes.EndEvent)
            ],
            [Flow("start", "xm"), Flow("xm", "ta"), Flow("ta", "tb"), Flow("tb", "xs"), Flow("xs", "xm"), Flow("xs", "end")]);

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "a", "b"));
        Assert.Empty(Analyze(process).UnorderedWrites);
    }

    // An optional step: one choice runs the step, the other skips straight to the merge. Only one token passes.
    [Fact]
    public void An_optional_step_skipped_by_an_exclusive_split_stays_ordered()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("x", BpmnElementTypes.ExclusiveGateway),
                Task("t2", "step"), Element("j", BpmnElementTypes.ExclusiveGateway), Task("t3", "after")
            ],
            [Flow("start", "x"), Flow("x", "t2"), Flow("x", "j"), Flow("t2", "j"), Flow("j", "t3")]);

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "after", "after"));
        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "step", "after"));
    }

    // A parallel split that sends one token straight back into the task and one around the outer loop runs the task
    // twice at once.
    [Fact]
    public void A_parallel_split_sending_both_branches_back_into_a_task_overlaps_it()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("d", BpmnElementTypes.ExclusiveGateway),
                Task("t", "step"), Element("p", BpmnElementTypes.ParallelGateway)
            ],
            [Flow("start", "d"), Flow("d", "t"), Flow("t", "p"), Flow("p", "t"), Flow("p", "d")]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "step", "step"));
    }

    // A cycle with two ways in has no loop head: the runtime picks its back flows by flow-id order, so the process
    // declares nothing.
    [Fact]
    public void A_parallel_split_feeding_two_entries_of_a_loop_declares_nothing()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("p", BpmnElementTypes.ParallelGateway),
                Task("j", "join"), Element("a", BpmnElementTypes.ExclusiveGateway), Task("b", "back"), Element("end", BpmnElementTypes.EndEvent)
            ],
            [Flow("start", "p"), Flow("p", "j"), Flow("p", "b"), Flow("j", "a"), Flow("a", "b"), Flow("a", "end"), Flow("b", "j")]);

        Assert.Equal(ActivityChildTimeRelation.Unspecified, _handler.RelateChildren(process, "join", "join"));
    }

    // A non-interrupting boundary runs beside its host's normal flow, also when another boundary interrupts: on a loop,
    // the boundary's path overlaps the next pass.
    [Fact]
    public void A_non_interrupting_boundary_beside_an_interrupting_one_on_a_loop_overlaps_the_next_pass()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("m", BpmnElementTypes.ExclusiveGateway), Task("h", "host"),
                new BpmnElement("i", BpmnElementTypes.BoundaryEvent, attachedToRef: "h", cancelActivity: true),
                new BpmnElement("n", BpmnElementTypes.BoundaryEvent, attachedToRef: "h", cancelActivity: false),
                Element("x", BpmnElementTypes.ExclusiveGateway), Task("w", "side"), Element("end", BpmnElementTypes.EndEvent)
            ],
            [Flow("start", "m"), Flow("m", "h"), Flow("h", "x"), Flow("x", "m"), Flow("x", "end"), Flow("n", "w"), Flow("i", "end")]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "side", "side"));
    }

    // A cycle with two ways in has no loop head: the runtime picks its back flows by flow-id order, so the process
    // declares nothing.
    [Theory]
    [InlineData(BpmnElementTypes.InclusiveGateway)]
    [InlineData(BpmnElementTypes.ParallelGateway)]
    public void A_parallel_branch_awaited_inside_a_two_entry_loop_declares_nothing(string waitingJoin)
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("p", BpmnElementTypes.ParallelGateway), Task("j", "join"),
                Task("b", "side"), Element("w", waitingJoin), Element("x", BpmnElementTypes.ExclusiveGateway), Element("end", BpmnElementTypes.EndEvent)
            ],
            [Flow("start", "p"), Flow("p", "j"), Flow("p", "b"), Flow("j", "w"), Flow("b", "w"), Flow("w", "x"), Flow("x", "j"), Flow("x", "end")]);

        Assert.Equal(ActivityChildTimeRelation.Unspecified, _handler.RelateChildren(process, "join", "join"));
    }

    // A cycle with two ways in has no loop head: the runtime picks its back flows by flow-id order, so the process
    // declares nothing.
    [Theory]
    [InlineData(BpmnElementTypes.InclusiveGateway)]
    [InlineData(BpmnElementTypes.ParallelGateway)]
    public void A_loop_entered_after_a_waiting_join_and_at_its_task_declares_nothing(string waitingJoin)
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("p0", BpmnElementTypes.ParallelGateway),
                Element("p", BpmnElementTypes.ParallelGateway), Task("j", "join"), Task("b", "side"), Task("s", "early"),
                Element("w", waitingJoin), Task("y", "hop"), Task("x", "decide"), Element("end", BpmnElementTypes.EndEvent)
            ],
            [
                Flow("start", "p0"), Flow("p0", "p"), Flow("p0", "s"), Flow("p", "j"), Flow("p", "b"), Flow("b", "w"), Flow("s", "w"),
                Flow("w", "y"), Flow("y", "x"), Flow("j", "x"), Flow("x", "j"), Flow("x", "end")
            ]);

        Assert.Equal(ActivityChildTimeRelation.Unspecified, _handler.RelateChildren(process, "join", "join"));
    }

    // A fork after the task frees a waiting join with its own branches, while it also runs the task again.
    [Fact]
    public void A_waiting_join_fed_by_the_forks_own_branches_does_not_hold_them()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Task("d", "entry"), Task("j", "join"), Element("xg", BpmnElementTypes.ExclusiveGateway),
                Element("f", BpmnElementTypes.ParallelGateway), Task("s", "left"), Task("b", "right"), Element("w", BpmnElementTypes.ParallelGateway),
                Element("end", BpmnElementTypes.EndEvent)
            ],
            [
                Flow("start", "d"), Flow("d", "j"), Flow("j", "xg"), Flow("xg", "f"), Flow("xg", "end"), Flow("f", "j"), Flow("f", "s"),
                Flow("f", "b"), Flow("s", "w"), Flow("b", "w"), Flow("w", "d")
            ]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "join", "join"));
    }

    // A waiting join fed by a sibling branch of an earlier fork after the task: that branch's token is already there, so
    // the later fork's branch fires the waiting join while the fork also runs the task again.
    [Theory]
    [InlineData(BpmnElementTypes.InclusiveGateway)]
    [InlineData(BpmnElementTypes.ParallelGateway)]
    public void A_waiting_join_fed_by_an_earlier_forks_sibling_branch_does_not_hold_the_branch(string waitingJoin)
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Task("d", "entry"), Task("j", "join"), Element("xg", BpmnElementTypes.ExclusiveGateway),
                Element("p", BpmnElementTypes.ParallelGateway), Task("s", "left"), Element("f", BpmnElementTypes.ParallelGateway), Task("b", "right"),
                Element("w", waitingJoin), Element("end", BpmnElementTypes.EndEvent)
            ],
            [
                Flow("start", "d"), Flow("d", "j"), Flow("j", "xg"), Flow("xg", "p"), Flow("xg", "end"), Flow("p", "s"), Flow("p", "f"),
                Flow("f", "j"), Flow("f", "b"), Flow("s", "w"), Flow("b", "w"), Flow("w", "d")
            ]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "join", "join"));
    }

    // A retry loop on one branch of a parallel split sends its token to the inclusive join in a later iteration than the
    // other branch's: the join fires once per token, so what follows it runs twice at once.
    [Fact]
    public void An_inclusive_join_after_a_retry_loop_on_one_branch_fires_per_token()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("fork", BpmnElementTypes.ParallelGateway), Task("a", "left"),
                Task("y", "retry"), Element("gw", BpmnElementTypes.ExclusiveGateway), Element("w", BpmnElementTypes.InclusiveGateway),
                Task("b", "after"), Element("end", BpmnElementTypes.EndEvent)
            ],
            [
                Flow("start", "fork"), Flow("fork", "a"), Flow("a", "w"), Flow("fork", "y"), Flow("y", "gw"), Flow("gw", "y"), Flow("gw", "w"),
                Flow("w", "b"), Flow("b", "end")
            ]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "after", "after"));
    }

    // The same retry loop before a parallel join: the keys never pair, the join never fires, so nothing runs twice.
    [Fact]
    public void A_parallel_join_after_a_retry_loop_on_one_branch_stays_a_waiting_join()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("fork", BpmnElementTypes.ParallelGateway), Task("a", "left"),
                Task("y", "retry"), Element("gw", BpmnElementTypes.ExclusiveGateway), Element("w", BpmnElementTypes.ParallelGateway),
                Task("b", "after"), Element("end", BpmnElementTypes.EndEvent)
            ],
            [
                Flow("start", "fork"), Flow("fork", "a"), Flow("a", "w"), Flow("fork", "y"), Flow("y", "gw"), Flow("gw", "y"), Flow("gw", "w"),
                Flow("w", "b"), Flow("b", "end")
            ]);

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "after", "after"));
    }

    // Two parallel blocks in one loop body, the second one optional: its branches cross no back flow before its join, so
    // they pair, and the earlier block's branches merge at their own join first.
    [Theory]
    [InlineData(BpmnElementTypes.InclusiveGateway)]
    [InlineData(BpmnElementTypes.ParallelGateway)]
    public void An_optional_parallel_block_after_another_inside_a_loop_stays_ordered(string waitingJoin)
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("m", BpmnElementTypes.ExclusiveGateway),
                Element("f1", BpmnElementTypes.ParallelGateway), Task("a1", "check-a"), Task("b1", "check-b"), Element("w1", BpmnElementTypes.ParallelGateway),
                Element("g2", BpmnElementTypes.ExclusiveGateway), Element("f2", BpmnElementTypes.ParallelGateway), Task("a2", "left"), Task("b2", "right"),
                Element("w2", waitingJoin), Task("t", "after"), Element("x", BpmnElementTypes.ExclusiveGateway), Element("end", BpmnElementTypes.EndEvent)
            ],
            [
                Flow("start", "m"), Flow("m", "f1"), Flow("f1", "a1"), Flow("f1", "b1"), Flow("a1", "w1"), Flow("b1", "w1"), Flow("w1", "g2"),
                Flow("g2", "f2"), Flow("f2", "a2"), Flow("f2", "b2"), Flow("a2", "w2"), Flow("b2", "w2"), Flow("w2", "t"), Flow("t", "x"),
                Flow("g2", "x"), Flow("x", "m"), Flow("x", "end")
            ]);

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "after", "after"));
        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "left", "after"));
    }

    // Rework that loops back into one branch of a parallel block enters that cycle at two places: the process declares
    // nothing.
    [Fact]
    public void Rework_looping_back_into_one_branch_of_a_parallel_block_declares_nothing()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("fork", BpmnElementTypes.ParallelGateway), Task("a", "left"),
                Task("b", "redo"), Element("w", BpmnElementTypes.InclusiveGateway), Task("c", "after"), Element("g", BpmnElementTypes.ExclusiveGateway),
                Element("end", BpmnElementTypes.EndEvent)
            ],
            [
                Flow("start", "fork"), Flow("fork", "a"), Flow("fork", "b"), Flow("a", "w"), Flow("b", "w"), Flow("w", "c"), Flow("c", "g"),
                Flow("g", "b"), Flow("g", "end")
            ]);

        Assert.Equal(ActivityChildTimeRelation.Unspecified, _handler.RelateChildren(process, "after", "after"));
    }

    // An inclusive merge that the other branch reaches only on one exclusive choice does not hold this branch: on the
    // other choice that branch retries and reaches the final join with a new key, so the final join fires per arrival.
    [Fact]
    public void An_inclusive_merge_reached_on_one_choice_does_not_hold_the_other_branch()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("p", BpmnElementTypes.ParallelGateway), Task("a", "left"),
                Element("m", BpmnElementTypes.InclusiveGateway), Task("c", "right"), Element("x", BpmnElementTypes.ExclusiveGateway),
                Task("z", "retry"), Element("g", BpmnElementTypes.ExclusiveGateway), Element("j", BpmnElementTypes.InclusiveGateway),
                Task("w", "after"), Element("end", BpmnElementTypes.EndEvent)
            ],
            [
                Flow("start", "p"), Flow("p", "a"), Flow("a", "m"), Flow("p", "c"), Flow("c", "x"), Flow("x", "m"), Flow("x", "z"),
                Flow("z", "g"), Flow("g", "z"), Flow("g", "j"), Flow("m", "j"), Flow("j", "w"), Flow("w", "end")
            ]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "after", "after"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "retry", "after"));
    }

    // A loop that only a boundary event leads into is walked by the runtime from its lowest element id, so its back flows
    // depend on ids: the process declares nothing.
    [Fact]
    public void A_loop_reached_only_from_a_boundary_event_declares_nothing()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Task("host", "host"),
                new BpmnElement("b", BpmnElementTypes.BoundaryEvent, attachedToRef: "host", cancelActivity: true),
                Element("h", BpmnElementTypes.ExclusiveGateway), Task("w", "work"), Element("g", BpmnElementTypes.ExclusiveGateway),
                Element("end", BpmnElementTypes.EndEvent), Element("end2", BpmnElementTypes.EndEvent)
            ],
            [Flow("start", "host"), Flow("host", "end"), Flow("b", "h"), Flow("h", "w"), Flow("w", "g"), Flow("g", "h"), Flow("g", "end2")]);

        Assert.Equal(ActivityChildTimeRelation.Unspecified, _handler.RelateChildren(process, "work", "work"));
    }

    // A reminder loop on a boundary path (a timer on the step leads back to the notification) is a cycle only through the
    // host's boundary, which the runtime's walk does not follow: the process still declares.
    [Fact]
    public void A_reminder_loop_on_a_boundary_path_still_declares()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Task("a", "work"), Element("end", BpmnElementTypes.EndEvent),
                new BpmnElement("ba", BpmnElementTypes.BoundaryEvent, attachedToRef: "a", cancelActivity: true),
                Task("notify", "notify"), Task("h2", "approve"), Element("end2", BpmnElementTypes.EndEvent),
                new BpmnElement("bt", BpmnElementTypes.BoundaryEvent, attachedToRef: "h2", cancelActivity: true)
            ],
            [Flow("start", "a"), Flow("a", "end"), Flow("ba", "notify"), Flow("notify", "h2"), Flow("h2", "end2"), Flow("bt", "notify")]);

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "notify", "approve"));
    }

    // A boundary path that returns into the started flow's loop is walked with that loop: the process still declares.
    [Fact]
    public void A_boundary_path_back_into_a_started_loop_still_declares()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("m", BpmnElementTypes.ExclusiveGateway), Task("host", "host"),
                new BpmnElement("b", BpmnElementTypes.BoundaryEvent, attachedToRef: "host", cancelActivity: true),
                Task("fix", "fix"), Element("x", BpmnElementTypes.ExclusiveGateway), Element("end", BpmnElementTypes.EndEvent)
            ],
            [Flow("start", "m"), Flow("m", "host"), Flow("host", "x"), Flow("x", "m"), Flow("x", "end"), Flow("b", "fix"), Flow("fix", "m")]);

        Assert.NotEqual(ActivityChildTimeRelation.Unspecified, _handler.RelateChildren(process, "host", "host"));
    }

    // A parallel split and its waiting join inside a loop body stay one sequential pass.
    [Theory]
    [InlineData(BpmnElementTypes.InclusiveGateway)]
    [InlineData(BpmnElementTypes.ParallelGateway)]
    public void A_split_and_waiting_join_inside_a_loop_body_stay_ordered(string waitingJoin)
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("m", BpmnElementTypes.ExclusiveGateway), Task("h", "head"),
                Element("p", BpmnElementTypes.ParallelGateway), Task("l", "left"), Task("r", "right"), Element("w", waitingJoin),
                Task("t", "after"), Element("x", BpmnElementTypes.ExclusiveGateway), Element("end", BpmnElementTypes.EndEvent)
            ],
            [
                Flow("start", "m"), Flow("m", "h"), Flow("h", "p"), Flow("p", "l"), Flow("p", "r"), Flow("l", "w"), Flow("r", "w"),
                Flow("w", "t"), Flow("t", "x"), Flow("x", "m"), Flow("x", "end")
            ]);

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "after", "after"));
        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "left", "after"));
    }

    // Link events connect without sequence flows, which the analysis does not model: the process declares nothing.
    [Fact]
    public void A_process_with_link_events_declares_nothing()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Task("x", "left"),
                new BpmnElement("throw", BpmnElementTypes.IntermediateThrowEvent, eventDefinitions: [new BpmnEventDefinition("link")]),
                new BpmnElement("catch", BpmnElementTypes.IntermediateCatchEvent, eventDefinitions: [new BpmnEventDefinition("link")]),
                Task("b", "after")
            ],
            [Flow("start", "x"), Flow("x", "throw"), Flow("catch", "b")]);

        Assert.Equal(ActivityChildTimeRelation.Unspecified, _handler.RelateChildren(process, "left", "after"));
    }

    // A non-interrupting boundary that leads back into its host runs the host again while it is still active.
    [Fact]
    public void A_non_interrupting_boundary_leading_back_into_its_host_overlaps_the_host()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Task("h", "host"),
                new BpmnElement("i", BpmnElementTypes.BoundaryEvent, attachedToRef: "h", cancelActivity: true),
                new BpmnElement("n", BpmnElementTypes.BoundaryEvent, attachedToRef: "h", cancelActivity: false),
                Task("a", "again"), Element("end", BpmnElementTypes.EndEvent)
            ],
            [Flow("start", "h"), Flow("h", "end"), Flow("i", "end"), Flow("n", "a"), Flow("a", "h")]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "host", "host"));
    }

    // Nested loops closed by exclusive merges pass one token around: the inner body stays sequential.
    [Fact]
    public void Nested_loops_closed_by_exclusive_merges_stay_ordered()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Element("o", BpmnElementTypes.ExclusiveGateway),
                Element("i", BpmnElementTypes.ExclusiveGateway), Task("ta", "a"), Element("xs", BpmnElementTypes.ExclusiveGateway),
                Element("xo", BpmnElementTypes.ExclusiveGateway), Element("end", BpmnElementTypes.EndEvent)
            ],
            [Flow("start", "o"), Flow("o", "i"), Flow("i", "ta"), Flow("ta", "xs"), Flow("xs", "i"), Flow("xs", "xo"), Flow("xo", "o"), Flow("xo", "end")]);

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _handler.RelateChildren(process, "a", "a"));
    }

    [Fact]
    public void An_event_subprocess_is_unordered_with_the_process_flow()
    {
        var process = Process(
            [
                Element("start", BpmnElementTypes.StartEvent), Task("a", "main"),
                new BpmnElement("events", BpmnElementTypes.SubProcess, childNodeId: "on-event", triggeredByEvent: true)
            ],
            [Flow("start", "a")]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(process, "main", "on-event"));
    }

    [Fact]
    public void The_process_completes_after_its_children_and_unknown_children_are_not_declared()
    {
        var process = Split(BpmnElementTypes.ParallelGateway);

        Assert.True(_handler.CompletesAfterItsChildren(process));
        Assert.Equal(ActivityChildTimeRelation.Unspecified, _handler.RelateChildren(process, "left", "not-a-child"));
    }

    private UnorderedVariableWriteAnalysis Analyze(ActivityNode process) =>
        WriteOrderAnalyzer().Analyze(process, new DefaultActivityStructureService([_handler]), includeIntrinsics: true);

    private static Elsa.Workflows.Design.Validations.Core.Contracts.IUnorderedVariableWriteAnalyzer WriteOrderAnalyzer()
    {
        var services = new ServiceCollection();
        new Elsa.Workflows.Design.Validations.WorkflowDesignValidationsFeature().ConfigureServices(services);
        return services.BuildServiceProvider().GetRequiredService<Elsa.Workflows.Design.Validations.Core.Contracts.IUnorderedVariableWriteAnalyzer>();
    }


    private static ActivityNode Split(string gatewayType) => Process(
        [Element("start", BpmnElementTypes.StartEvent), Element("split", gatewayType), Task("a", "left"), Task("b", "right"), Element("end", BpmnElementTypes.EndEvent)],
        [Flow("start", "split"), Flow("split", "a"), Flow("split", "b"), Flow("a", "end"), Flow("b", "end")]);

    private static ActivityNode Process(IReadOnlyCollection<BpmnElement> elements, IReadOnlyCollection<BpmnSequenceFlow> flows)
    {
        var children = elements
            .SelectMany(element => new[] { element.ChildNodeId, element.ListenerNodeId })
            .OfType<string>()
            .Select(id => new ActivityNode(id, "test@1", [], [new ArgumentState("result",
                new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "total", declaringScopeId = "workflow" }), "Variable"),
                null, null, null, null)]))
            .ToArray();
        var structure = new BpmnAuthoredStructure(children, elements, flows);
        return new ActivityNode("process", "bpmn@1", [], [],
            new(BpmnProcessActivity.StructureKind, BpmnProcessActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, SerializerOptions)));
    }

    private static BpmnElement Element(string id, string type) => new(id, type);

    private static BpmnElement Task(string id, string childNodeId) => new(id, BpmnElementTypes.Task, childNodeId: childNodeId);

    private static BpmnSequenceFlow Flow(string source, string target) => new($"{source}->{target}", source, target);
}
