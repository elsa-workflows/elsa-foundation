using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Scheduling.Tests;

internal static class DelayTestExecutable
{
    public const string ActivityExecutionId = "actexec-delay";
    public const string TimerId = "timer:" + ActivityExecutionId;
    public const string BookmarkId = ActivityExecutionId + ":attempt:1:trigger:1";
    private const string NodeId = "node-delay";
    private const string ResumeTargetId = "resume-target:durable-timer";

    public static WorkflowExecutable Create(TimeSpan duration, DateTimeOffset createdAt)
    {
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var alias = TypeAliasConvention.CanonicalAlias(typeof(Delay));
        var descriptor = serializer.SerializeToElement(new ClrActivityDescriptor(alias));
        var contract = new ActivityContract(
            alias,
            "1.0.0",
            typeof(ClrActivityDescriptor).FullName!,
            descriptor,
            [new ActivityInputContract(
                "Duration",
                nameof(Delay.Duration),
                new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(TimeSpan))),
                false,
                true,
                JsonSerializer.SerializeToElement(TimeSpan.Zero),
                ActivityValuePolicy.Default)],
            new ActivityResultContract(
                new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(ActivityUnit))),
                true,
                ActivityValuePolicy.Default,
                []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(typeof(ClrActivityDescriptor).FullName!, alias));
        var root = new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: "authored-delay",
            activityType: alias,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(ClrActivityDescriptor).FullName!,
            descriptorPayload: descriptor,
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Duration"] = new RuntimeInputBinding(
                    inputKey: "Duration",
                    targetType: new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(TimeSpan))),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(
                        new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(TimeSpan))),
                        JsonSerializer.SerializeToElement(duration),
                        ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(),
            activityContract: contract);

        return new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [ResumeTargetId] = new(
                    ResumeTargetId,
                    NodeId,
                    "ResumeAsync",
                    new Dictionary<string, string>())
            },
            createdAt: createdAt,
            compatibilityMetadata: new Dictionary<string, string>());
    }
}
