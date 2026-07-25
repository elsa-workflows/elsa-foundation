using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// Publish-time extraction and schedule tests for the BPMN event-defined start surface (spec 117 D1/D2/D3).
/// </summary>
public sealed class BpmnEventStartTriggerTests
{
    private readonly BpmnProcessTriggerStimulusProvider _stimulusProvider = new();
    private readonly BpmnProcessRecurringScheduleProvider _scheduleProvider = new();

    [Fact]
    public void Extract_IndexesOneBindingPerEventDefinedStart_WithElementIdMetadata()
    {
        var executable = TriggerExecutable(
            elements:
            [
                MessageStart("msg-start", "order-placed"),
                SignalStart("sig-start", "cancel-requested"),
                TimerStart("timer-start", interval: "PT5M"),
                End("end")
            ],
            flows:
            [
                Flow("f1", "msg-start", "end"),
                Flow("f2", "sig-start", "end"),
                Flow("f3", "timer-start", "end")
            ]);

        var bindings = new WorkflowTriggerBindingExtractor([_stimulusProvider]).Extract(executable);

        Assert.Equal(3, bindings.Count);
        Assert.All(bindings, binding => Assert.Equal(BpmnRuntimeFixture.ProcessNodeId, binding.ExecutableNodeId));

        var byElement = bindings.ToDictionary(binding => binding.Metadata[BpmnStartTrigger.StartElementIdMetadataKey]);
        Assert.Equal(new[] { "msg-start", "sig-start", "timer-start" }, byElement.Keys.OrderBy(x => x));

        Assert.Equal(BpmnMessageStartStimulus.StimulusType, byElement["msg-start"].StimulusType);
        Assert.Equal(BpmnMessageStartStimulus.Hash("order-placed"), byElement["msg-start"].StimulusHash);
        Assert.Equal(BpmnMessageStartStimulus.Hash("cancel-requested"), byElement["sig-start"].StimulusHash);
        Assert.Equal(BpmnTimerStartStimulus.StimulusType, byElement["timer-start"].StimulusType);
    }

    [Fact]
    public void Extract_IntentionallyNonStarting_WhenNoEventDefinedStarts()
    {
        var executable = TriggerExecutable(
            elements: [NoneStart("start"), End("end")],
            flows: [Flow("f1", "start", "end")]);

        var outcome = new WorkflowTriggerBindingExtractor([_stimulusProvider]).Evaluate(executable);

        var node = Assert.Single(outcome.NodeOutcomes);
        Assert.Equal(WorkflowTriggerPreflightStatus.IntentionallyNonStarting, node.Status);
        Assert.Empty(node.Bindings);
    }

    [Fact]
    public void Extract_IntentionallyNonStarting_WhenNestedProcessCannotStart()
    {
        // A BpmnProcess bound as a child sets CanStartWorkflow = false, so its event-defined starts register
        // no bindings even though the extractor still classifies the node as a trigger (spec 117 D1).
        var executable = TriggerExecutable(
            elements: [MessageStart("msg-start", "order-placed"), End("end")],
            flows: [Flow("f1", "msg-start", "end")],
            canStartWorkflow: false);

        var outcome = new WorkflowTriggerBindingExtractor([_stimulusProvider]).Evaluate(executable);

        var node = Assert.Single(outcome.NodeOutcomes);
        Assert.Equal(WorkflowTriggerPreflightStatus.IntentionallyNonStarting, node.Status);
        Assert.Empty(node.Bindings);
    }

    [Fact]
    public void Extract_Indexes_WhenCanStartWorkflowExplicitlyTrue()
    {
        var executable = TriggerExecutable(
            elements: [MessageStart("msg-start", "order-placed"), End("end")],
            flows: [Flow("f1", "msg-start", "end")],
            canStartWorkflow: true);

        var binding = Assert.Single(new WorkflowTriggerBindingExtractor([_stimulusProvider]).Extract(executable));
        Assert.Equal(BpmnMessageStartStimulus.Hash("order-placed"), binding.StimulusHash);
    }

    [Fact]
    public void Extract_FailsPublish_WhenTwoStartsShareEventName()
    {
        var executable = TriggerExecutable(
            elements: [MessageStart("a", "duplicate"), MessageStart("b", "duplicate"), End("end")],
            flows: [Flow("f1", "a", "end"), Flow("f2", "b", "end")]);

        Assert.Throws<WorkflowTriggerPreflightException>(() =>
            new WorkflowTriggerBindingExtractor([_stimulusProvider]).Evaluate(executable));
    }

    [Fact]
    public void Extract_FailsPublish_WhenMessageStartHasNoName()
    {
        var executable = TriggerExecutable(
            elements: [Start("bad", BpmnEventDefinitionTypes.Message, properties: null), End("end")],
            flows: [Flow("f1", "bad", "end")]);

        Assert.Throws<WorkflowTriggerPreflightException>(() =>
            new WorkflowTriggerBindingExtractor([_stimulusProvider]).Evaluate(executable));
    }

    [Theory]
    [InlineData(null, null)] // neither
    [InlineData("PT5M", "0 * * * *")] // both
    public void Extract_FailsPublish_WhenTimerExpressionInvalid(string? interval, string? cron)
    {
        var properties = new Dictionary<string, string>();
        if (interval is not null) properties[BpmnEventDefinitionProperties.Interval] = interval;
        if (cron is not null) properties[BpmnEventDefinitionProperties.Cron] = cron;

        var executable = TriggerExecutable(
            elements: [Start("timer-start", BpmnEventDefinitionTypes.Timer, properties), End("end")],
            flows: [Flow("f1", "timer-start", "end")]);

        Assert.Throws<WorkflowTriggerPreflightException>(() =>
            new WorkflowTriggerBindingExtractor([_stimulusProvider]).Evaluate(executable));
    }

    [Fact]
    public void ScheduleProvider_FansOutOneDescriptorPerTimerStart_WithTriggerParity()
    {
        var node = TriggerNode(
            elements:
            [
                TimerStart("timer-a", interval: "PT5M"),
                TimerStart("timer-b", cron: "0 * * * *"),
                MessageStart("msg", "order-placed"),
                End("end")
            ],
            flows:
            [
                Flow("f1", "timer-a", "end"),
                Flow("f2", "timer-b", "end"),
                Flow("f3", "msg", "end")
            ]);

        var schedules = _scheduleProvider.Describe(node);
        Assert.Equal(2, schedules.Count);

        // Every timer schedule's (type, hash) must equal the stimulus provider's binding for the same element.
        var triggerByHash = _stimulusProvider.Describe(node).Descriptors
            .ToDictionary(descriptor => descriptor.StimulusHash);
        foreach (var schedule in schedules)
        {
            Assert.Equal(BpmnTimerStartStimulus.StimulusType, schedule.StimulusType);
            Assert.True(triggerByHash.ContainsKey(schedule.StimulusHash), "Schedule hash has no matching trigger descriptor.");
            Assert.Equal(schedule.StimulusType, triggerByHash[schedule.StimulusHash].StimulusType);
        }

        Assert.Contains(schedules, s => s.Kind == RecurringScheduleKind.Interval && s.Expression == "PT5M");
        Assert.Contains(schedules, s => s.Kind == RecurringScheduleKind.Cron && s.Expression == "0 * * * *");
    }

    [Fact]
    public void ScheduleProvider_ReturnsEmpty_ForForeignActivityType_AndNonStartingProcess()
    {
        var timerNode = TriggerNode(
            elements: [TimerStart("timer", interval: "PT5M"), End("end")],
            flows: [Flow("f1", "timer", "end")],
            canStartWorkflow: false);

        Assert.Empty(_scheduleProvider.Describe(timerNode));
    }

    [Fact]
    public void MessageStartStimulus_MatchesEventStimulus_ForRoutingParity()
    {
        // The whole point of D2: one raised Event delivery both starts BPMN message/signal processes and resumes
        // catch events. The BPMN derivation MUST resolve to the same (type, hash) EventStimulus produces.
        Assert.Equal(EventStimulus.StimulusType, BpmnMessageStartStimulus.StimulusType);
        Assert.Equal(EventStimulus.Hash("order-placed"), BpmnMessageStartStimulus.Hash("order-placed"));
        Assert.Equal(EventStimulus.Hash("cancel-requested"), BpmnMessageStartStimulus.Hash("cancel-requested"));
    }

    [Fact]
    public void TimerStartStimulus_IsIsolatedFromTimerAndCronActivities_AndPerElement()
    {
        var a = BpmnTimerStartStimulus.Hash("timer-a", "PT5M");
        var b = BpmnTimerStartStimulus.Hash("timer-b", "PT5M");

        Assert.NotEqual("Timer", BpmnTimerStartStimulus.StimulusType);
        Assert.NotEqual("Cron", BpmnTimerStartStimulus.StimulusType);
        Assert.NotEqual(a, b); // element id folded in → per-element uniqueness even for identical intervals
        Assert.NotEqual(TimerStimulus.Hash("PT5M"), a); // isolated from the Timer activity's interval-only hash
    }

    // ---- builders ----

    private static WorkflowExecutable TriggerExecutable(
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> flows,
        bool? canStartWorkflow = null) =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-bpmn", "definition-1", "version-1", "1.0.0", "sha256:v1"),
            rootActivity: TriggerNode(elements, flows, canStartWorkflow),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static ExecutableNode TriggerNode(
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> flows,
        bool? canStartWorkflow = null)
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        if (canStartWorkflow is { } value)
            bindings[nameof(BpmnProcessActivity.CanStartWorkflow)] = LiteralBinding(nameof(BpmnProcessActivity.CanStartWorkflow), value);

        return new ExecutableNode(
            executableNodeId: BpmnRuntimeFixture.ProcessNodeId,
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
            inputBindings: bindings,
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType },
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, [])],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(elements, flows))));
    }

    private static RuntimeInputBinding LiteralBinding(string name, object value)
    {
        var type = new ValueTypeDescriptor(value is bool ? "Boolean" : "String");
        return new RuntimeInputBinding(
            name,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    private static BpmnElement NoneStart(string id) => new(id, BpmnElementTypes.StartEvent);
    private static BpmnElement End(string id) => new(id, BpmnElementTypes.EndEvent);

    private static BpmnElement Start(string id, string definitionType, IReadOnlyDictionary<string, string>? properties) =>
        new(id, BpmnElementTypes.StartEvent, eventDefinitions: [new BpmnEventDefinition(definitionType, properties)]);

    private static BpmnElement MessageStart(string id, string name) =>
        Start(id, BpmnEventDefinitionTypes.Message, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = name });

    private static BpmnElement SignalStart(string id, string name) =>
        Start(id, BpmnEventDefinitionTypes.Signal, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = name });

    private static BpmnElement TimerStart(string id, string? interval = null, string? cron = null)
    {
        var properties = new Dictionary<string, string>();
        if (interval is not null) properties[BpmnEventDefinitionProperties.Interval] = interval;
        if (cron is not null) properties[BpmnEventDefinitionProperties.Cron] = cron;
        return Start(id, BpmnEventDefinitionTypes.Timer, properties);
    }

    private static BpmnSequenceFlow Flow(string id, string source, string target) => new(id, source, target);
}
