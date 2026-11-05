using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.StateMachine.M0.Witness;

internal static class CauseExecutable
{
    internal const string ChildDelayed = "cause-child-delayed";
    internal const string ParentDelayed = "cause-parent-delayed";
    internal const string Distinct = "cause-distinct";
    internal const string WrongRelay = "cause-wrong-relay";
    internal static bool IsDistinct(string scenario) => scenario == Distinct;
    internal static string? HeldSource(string scenario) => scenario == Distinct ? null : scenario == ParentDelayed ? "parent" : "child";
    internal static string BusinessName(string source, bool distinct) => distinct ? $"m0-cause-distinct-{source}" : CauseProbeNames.CommonEvent;
    internal static JsonElement Payload(string id) => JsonSerializer.SerializeToElement(new { id, content = new { value = "same-public-content" } });

    internal static WorkflowExecutable Create(string scenario)
    {
        if (scenario is not (ChildDelayed or ParentDelayed or Distinct or WrongRelay)) throw new ArgumentException("Unknown cause scenario.");
        var distinct = IsDistinct(scenario);
        var jsonAlias = TypeAliasConvention.CanonicalAlias(typeof(JsonElement));
        var targets = new Dictionary<string, WorkflowExecutableResumeTarget>();
        RuntimeInputBinding Literal(string key, string alias, object value) => new(key, new ValueTypeDescriptor(alias), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(new ValueTypeDescriptor(alias), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
        RuntimeInputBinding Correlation(string key) => new(key, new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead, variable: new RuntimeVariableReference(CauseProbeNames.Correlation, CauseProbeNames.Machine));
        RuntimeInputBinding Result(string key, string alias, string producer, string projection = "$result") => new(key, new ValueTypeDescriptor(alias),
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference(producer, projection, "scope:root"));
        ExecutableNode Plain(string id, string type, IReadOnlyDictionary<string, RuntimeInputBinding> inputs) =>
            new(id, "authored-" + id, type, "1.0.0", "test", JsonSerializer.SerializeToElement(new { type = "test" }), inputs, new Dictionary<string, string>());
        ExecutableNode Wait(string id, string eventName, bool machineCorrelation)
        {
            var node = Plain(id, Event.ActivityType, new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(Event.EventName)] = Literal(nameof(Event.EventName), "String", eventName),
                [nameof(Event.CanStartWorkflow)] = Literal(nameof(Event.CanStartWorkflow), "Boolean", false),
                [nameof(Event.CorrelationId)] = machineCorrelation ? Correlation(nameof(Event.CorrelationId)) : Literal(nameof(Event.CorrelationId), "String", "unrelated-machine")
            });
            var idScoped = WorkflowExecutableResumeTarget.ComposeScopedId(id, Event.ResumeTargetId);
            targets[idScoped] = new(idScoped, id, "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId);
            return node;
        }
        ExecutableNode Container(string id, Type type, string kind, ExecutableNode[] children, object structure) =>
            new(id, "authored-" + id, type.FullName!, "1.0.0", "test", JsonSerializer.SerializeToElement(new { type = "test" }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), [new ExecutableChildSlot(CauseProbeNames.Slot, children)],
                new ExecutableActivityStructure(kind, "1.0.0", JsonSerializer.SerializeToElement(structure, ProbeNames.Json)));
        ExecutableNode Trigger(string source)
        {
            var business = Wait(CauseProbeNames.Event(source), BusinessName(source, distinct), true);
            var first = Plain(CauseProbeNames.FirstRelay(source), typeof(CausePayloadRelay).FullName!, new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(CausePayloadRelay.Payload)] = Result(nameof(CausePayloadRelay.Payload), jsonAlias, business.ExecutableNodeId, "Payload")
            });
            var work = new List<ExecutableNode> { business, first };
            if (HeldSource(scenario) == source) work.Add(Wait(CauseProbeNames.Gate(source), $"m0-cause-release-{source}", true));
            var corrupt = scenario == WrongRelay && HeldSource(scenario) == source;
            work.Add(Plain(CauseProbeNames.FinalRelay(source), typeof(CausePayloadRelay).FullName!, new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(CausePayloadRelay.Payload)] = Result(nameof(CausePayloadRelay.Payload), jsonAlias,
                    corrupt ? CauseProbeNames.Gate(source) : first.ExecutableNodeId, corrupt ? "Payload" : "$result")
            }));
            var body = new ExecutableNode(CauseProbeNames.Body(source), "authored-" + CauseProbeNames.Body(source), typeof(SequenceActivity).FullName!,
                "1.0.0", "test", JsonSerializer.SerializeToElement(new { type = "test" }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
                [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, work)], new ExecutableActivityStructure(SequenceActivity.StructureKind,
                    SequenceActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(new { activities = work.Select(item => item.ExecutableNodeId).ToArray() })));
            return Container(CauseProbeNames.Trigger(source), typeof(CauseTriggerProbe), "m0.cause-trigger", [body], new { activities = new[] { body.ExecutableNodeId } });
        }
        ExecutableNode Publisher(string id, string source, string inputId) => Plain(id, PublishEvent.ActivityType,
            new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(PublishEvent.EventName)] = Literal(nameof(PublishEvent.EventName), "String", BusinessName(source, distinct)),
                [nameof(PublishEvent.IsLocalEvent)] = Literal(nameof(PublishEvent.IsLocalEvent), "Boolean", true),
                [nameof(PublishEvent.CorrelationId)] = Correlation(nameof(PublishEvent.CorrelationId)),
                [nameof(PublishEvent.Payload)] = Literal(nameof(PublishEvent.Payload), jsonAlias, Payload(inputId))
            });
        var owner = Plain(CauseProbeNames.Owner, typeof(CauseOwnerProbe).FullName!, new Dictionary<string, RuntimeInputBinding>());
        var set = new ExecutableNode(CauseProbeNames.SetOwner, "authored-" + CauseProbeNames.SetOwner, "elsa.intrinsic.set", "1.0.0", "intrinsic",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>
            {
                ["value"] = Result("value", "String", CauseProbeNames.Owner)
            }, new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference(CauseProbeNames.Correlation, CauseProbeNames.Machine));
        var children = new List<ExecutableNode> { owner, set, Trigger("child"), Trigger("parent"),
            Wait(CauseProbeNames.PublishControl, "m0-cause-publish-control", true), Publisher(CauseProbeNames.PublisherA, "child", "A") };
        if (distinct) children.Add(Publisher(CauseProbeNames.PublisherB, "parent", "B"));
        var machine = Container(CauseProbeNames.Machine, typeof(CauseMachineProbe), "m0.cause-machine", children.ToArray(), new
        {
            distinct,
            activities = children.Select(item => item.ExecutableNodeId).ToArray(),
            variables = new[] { new RuntimeVariableDeclaration(CauseProbeNames.Correlation, CauseProbeNames.Correlation, new ValueTypeDescriptor("String"),
                ValueProtectionPolicy.InstanceInline, Literal(CauseProbeNames.Correlation, "String", "not-yet-assigned")) }
        });
        var ambient = Wait(CauseProbeNames.Ambient, BusinessName("child", distinct), false);
        var host = Container(CauseProbeNames.Host, typeof(CauseHostProbe), "m0.cause-host", [ambient, machine],
            new { activities = new[] { ambient.ExecutableNodeId, machine.ExecutableNodeId } });
        return new(WorkflowExecutionHarness.Identity, host, targets, WorkflowExecutionHarness.Timestamp,
            new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }
}
