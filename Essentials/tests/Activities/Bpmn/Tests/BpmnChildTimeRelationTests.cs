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
