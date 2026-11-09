using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>A one-node executable whose only node is a mid-flow <see cref="Event"/> wait on the given name.</summary>
internal static class EventWaitExecutable
{
    private const string ExecutableNodeId = "node-event";

    public static WorkflowExecutable Create(WorkflowExecutableIdentity identity, string eventName)
    {
        var resumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(ExecutableNodeId, Event.ResumeTargetId);
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var node = new ExecutableNode(
            executableNodeId: ExecutableNodeId,
            authoredActivityId: "authored-node-event",
            activityType: Event.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(Event.EventName)] = Literal(nameof(Event.EventName), eventName),
                [nameof(Event.CanStartWorkflow)] = Literal(nameof(Event.CanStartWorkflow), false)
            },
            metadata: new Dictionary<string, string>());

        return new WorkflowExecutable(
            identity,
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [resumeTargetId] = new(resumeTargetId, ExecutableNodeId, "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId)
            },
            new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static RuntimeInputBinding Literal(string inputName, object value)
    {
        var type = new ValueTypeDescriptor(value is bool ? "Boolean" : "String");
        return new RuntimeInputBinding(
            inputName,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }
}
