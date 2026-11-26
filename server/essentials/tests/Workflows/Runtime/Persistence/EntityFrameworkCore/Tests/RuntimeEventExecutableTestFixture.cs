using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static class RuntimeEventExecutableTestFixture
{
    /// <summary>
    /// An executable whose root waits on an event. A later <paramref name="version"/> is another artifact of the same workflow
    /// definition with the same node, as a republish produces.
    /// </summary>
    public static WorkflowExecutable Create(string prefix, int version = 1)
    {
        var nodeId = $"{prefix}-event";
        var resumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(nodeId, Event.ResumeTargetId);
        var clrContract = ClrActivityContractTestBuilder.BuildContract(typeof(Event));
        var contract = new ActivityContract(
            Event.ActivityType,
            clrContract.ContractVersion,
            clrContract.DescriptorKind,
            clrContract.DescriptorPayload,
            clrContract.Inputs.Values,
            clrContract.Result,
            clrContract.Outcomes,
            clrContract.Activation);
        var node = new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: Event.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: ClrActivityContractTestBuilder.CompleteInputBindings(
                contract,
                new Dictionary<string, RuntimeInputBinding>
                {
                    [nameof(Event.EventName)] = Literal(nameof(Event.EventName), "String", $"{prefix}-ready"),
                    [nameof(Event.CanStartWorkflow)] = Literal(nameof(Event.CanStartWorkflow), "Boolean", false)
                }),
            metadata: new Dictionary<string, string>(),
            activityContract: contract);

        return new WorkflowExecutable(
            version == 1
                ? new WorkflowExecutableIdentity($"{nodeId}-artifact", $"{prefix}-definition", $"{prefix}-version", "1.0.0", $"{prefix}-hash")
                : new WorkflowExecutableIdentity($"{nodeId}-artifact-v{version}", $"{prefix}-definition", $"{prefix}-version-{version}", $"{version}.0.0", $"{prefix}-hash-v{version}"),
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [resumeTargetId] = new(resumeTargetId, nodeId, "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId)
            },
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static RuntimeInputBinding Literal(string inputName, string typeAlias, object value)
    {
        var type = new ValueTypeDescriptor(typeAlias);
        return new RuntimeInputBinding(
            inputName,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }
}
