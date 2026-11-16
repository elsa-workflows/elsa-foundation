using System.Text.Json;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnChildResultFactsTests
{
    [Fact]
    public void Linear_results_follow_sequence_flows_not_the_authored_array_and_keep_default_cancel_flags()
    {
        using var provider = Provider();
        var structures = new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>());
        var process = Process(); // Authored array deliberately puts capture before decision.
        var facts = structures.AssessChildResult(process, "decision", "capture");
        Assert.Equal(ActivityChildResultAvailability.Definite, facts.Availability);
        Assert.False(facts.MayRunConcurrently);
        Assert.False(facts.IsCyclicBackEdge);
        Assert.False(facts.EarlierProducerRunCanReachConsumer);
        Assert.Equal(["decision"], structures.ChildrenCompletedBefore(process, "capture"));
        Assert.Empty(structures.ChildrenCompletedBefore(process, "decision")!);
        Assert.Equal(ActivityChildResultAvailability.Unavailable, structures.AssessChildResult(process, "capture", "decision").Availability);
        // A task of the proven chain runs once per process run, so the process cannot run it again without running
        // the whole process again, which is the enclosing structure's to say.
        var self = structures.AssessChildResult(process, "decision", "decision");
        Assert.Equal(ActivityChildResultAvailability.Unavailable, self.Availability);
        Assert.False(self.EarlierProducerRunCanReachConsumer);
        Assert.Equal(ActivityChildResultAvailability.Unknown, structures.AssessChildResult(process, "Decision", "capture").Availability);
        Assert.Null(structures.ChildrenCompletedBefore(process, "missing"));
    }

    [Theory]
    [InlineData("fork")]
    [InlineData("join")]
    [InlineData("cycle")]
    [InlineData("disconnected")]
    [InlineData("multiple-start")]
    [InlineData("duplicate-element")]
    [InlineData("duplicate-flow")]
    [InlineData("duplicate-child")]
    [InlineData("duplicate-activity")]
    [InlineData("unbound")]
    [InlineData("orphan")]
    [InlineData("condition")]
    [InlineData("default")]
    [InlineData("default-id")]
    [InlineData("event")]
    [InlineData("listener")]
    [InlineData("boundary")]
    [InlineData("loop")]
    [InlineData("compensation")]
    [InlineData("compensation-handler")]
    [InlineData("transaction")]
    [InlineData("event-subprocess")]
    [InlineData("properties")]
    [InlineData("subprocess")]
    [InlineData("call")]
    [InlineData("other-task-family")]
    public void Unproven_models_never_certify_order(string scenario)
    {
        using var provider = Provider();
        var structures = new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>());
        var process = Process(scenario);
        var facts = structures.AssessChildResult(process, "decision", "capture");
        Assert.Equal(ActivityChildResultAvailability.Unknown, facts.Availability);
        Assert.Null(facts.MayRunConcurrently);
        // Unproven occurrence semantics stay unstated, which refuses a result binding that names only the producer,
        // here and in every structure the process encloses.
        Assert.Null(facts.EarlierProducerRunCanReachConsumer);
        Assert.Null(structures.AssessChildResult(process, "decision", "decision").EarlierProducerRunCanReachConsumer);
        Assert.Null(structures.ChildrenCompletedBefore(process, "capture"));
    }

    /// <summary>A definite result always comes with the occurrence fact, so a binding is never offered on availability alone.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("cycle")]
    [InlineData("fork")]
    [InlineData("condition")]
    public void A_definite_result_always_states_whether_an_earlier_producer_run_can_reach_the_reader(string? scenario)
    {
        using var provider = Provider();
        var structures = new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>());
        var process = Process(scenario);
        foreach (var producer in new[] { "decision", "capture" })
        foreach (var consumer in new[] { "decision", "capture" })
        {
            var facts = structures.AssessChildResult(process, producer, consumer);
            if (facts.Availability == ActivityChildResultAvailability.Definite)
                Assert.NotNull(facts.EarlierProducerRunCanReachConsumer);
        }
    }

    internal static ActivityNode Process(string? scenario = null, IReadOnlyCollection<ActivityNode>? children = null)
    {
        var activities = children?.ToList() ?? [new("capture", "capture-version", [], []), new("decision", "decision-version", [], [])];
        var elements = new List<BpmnElement> { new("start", "startEvent"), new("task", "task", childNodeId: "decision"), new("capture-task", "task", childNodeId: "capture"), new("end", "endEvent") };
        var flows = new List<BpmnSequenceFlow> { new("f1", "start", "task"), new("f2", "task", "capture-task"), new("f3", "capture-task", "end") };
        switch (scenario)
        {
            case "fork":
                elements.Insert(1, new("fork", "parallelGateway")); elements.Insert(4, new("join", "parallelGateway"));
                flows = [new("f1", "start", "fork"), new("f2", "fork", "task"), new("f3", "fork", "capture-task"), new("f4", "task", "join"), new("f5", "capture-task", "join"), new("f6", "join", "end")]; break;
            case "join": flows.Add(new("bypass", "start", "capture-task")); break;
            case "cycle": flows[2] = new("f3", "capture-task", "task"); break;
            case "disconnected": elements.Add(new("extra", "endEvent")); flows.Add(new("f4", "extra", "extra")); break;
            case "multiple-start": elements.Add(new("other-start", "startEvent")); flows.Add(new("f4", "other-start", "capture-task")); break;
            case "duplicate-element": elements[2] = new("task", "task", childNodeId: "capture"); break;
            case "duplicate-flow": flows[2] = new("f2", "capture-task", "end"); break;
            case "duplicate-child": elements[2] = new("capture-task", "task", childNodeId: "decision"); break;
            case "duplicate-activity": activities.Add(new("decision", "duplicate", [], [])); break;
            case "unbound": elements[1] = new("task", "task"); break;
            case "orphan": activities.Add(new("orphan", "orphan-version", [], [])); break;
            case "condition": flows[1] = new("f2", "task", "capture-task", conditionOutcome: "Done"); break;
            case "default": flows[1] = new("f2", "task", "capture-task", isDefault: true); break;
            case "default-id": elements[1] = new("task", "task", childNodeId: "decision", defaultFlowId: "f2"); break;
            case "event": elements[0] = new("start", "startEvent", eventDefinitions: [new("message")]); break;
            case "listener": elements[1] = new("task", "task", childNodeId: "decision", listenerNodeId: "capture"); break;
            case "boundary": elements[1] = new("task", "boundaryEvent", childNodeId: "decision", attachedToRef: "capture-task"); break;
            case "loop": elements[1] = new("task", "task", childNodeId: "decision", loopCharacteristics: new(cardinality: 2)); break;
            case "compensation": elements[1] = new("task", "task", childNodeId: "decision", isForCompensation: true); break;
            case "compensation-handler": elements[1] = new("task", "task", childNodeId: "decision", compensationHandlerElementId: "capture-task"); break;
            case "event-subprocess": elements[1] = new("task", "subProcess", childNodeId: "decision", triggeredByEvent: true); break;
            case "properties": elements[1] = new("task", "task", childNodeId: "decision", properties: new Dictionary<string, string> { ["retained-semantics"] = "opaque" }); break;
            case "subprocess": elements[1] = new("task", "subProcess", childNodeId: "decision"); break;
            case "call": elements[1] = new("task", "callActivity", childNodeId: "decision"); break;
            case "other-task-family": elements[1] = new("task", "serviceTask", childNodeId: "decision"); break;
        }
        return new("process", "process-version", [], [], new("elsa.bpmn.structure", "1.0.0",
            JsonSerializer.SerializeToElement(new BpmnAuthoredStructure(activities, elements, flows, isTransaction: scenario == "transaction"), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }
}
