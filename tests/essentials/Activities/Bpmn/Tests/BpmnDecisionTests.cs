using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for <see cref="BpmnDecision"/>: the expression-condition evaluator leaf bound
/// to an exclusive gateway routes by its evaluated outcome, constructed through the production
/// transient CLR activation path.
/// </summary>
public sealed class BpmnDecisionTests
{
    private static readonly IPayloadSerializer Serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(params string[] activityExecutionIds) =>
        BpmnRuntimeFixture.CreateAsync(activityExecutionIds, services =>
        {
            new SerializationFeature().ConfigureServices(services);
            new ActivitiesPrimitivesFeature().ConfigureServices(services);
        });

    private static ExecutableNode NewDecisionNode(string nodeId, string outcome, params string[] declaredOutcomes)
    {
        var stringType = new ValueTypeDescriptor("String");
        // The authored contract declares the outcomes the decision can produce (the same way FlowSwitch
        // case outcomes are compiled into the node's contract at authoring time).
        var contract = new ActivityContract(
            typeof(BpmnDecision).FullName!,
            "1.0.0",
            ClrConstruction.DescriptorType,
            ClrConstruction.Payload(Serializer, typeof(BpmnDecision)),
            [new ActivityInputContract(nameof(BpmnDecision.Outcome), nameof(BpmnDecision.Outcome), stringType, false, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(
                new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(BpmnDecisionResult))),
                true,
                ActivityValuePolicy.Default,
                []),
            [.. declaredOutcomes, Workflows.Runtime.Core.Constants.ActivityOutcomes.Done],
            new ActivityActivationRequirement(ClrConstruction.DescriptorType, TypeAliasConvention.CanonicalAlias(typeof(BpmnDecision))));

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(BpmnDecision).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(Serializer, typeof(BpmnDecision)),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(BpmnDecision.Outcome)] = new(
                    inputKey: nameof(BpmnDecision.Outcome),
                    targetType: stringType,
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(stringType, JsonSerializer.SerializeToElement(outcome), ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }

    [Fact]
    public async Task ExclusiveGateway_WithBpmnDecision_RoutesByEvaluatedOutcome()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-decision", "actexec-high");
        var executable = fixture.NewExecutable(
            children:
            [
                NewDecisionNode("node-decision", outcome: "High", "High", "Low"),
                fixture.NewProbeNode("node-high"),
                fixture.NewProbeNode("node-low")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ExclusiveGateway("decision", childNodeId: "node-decision"),
                BpmnRuntimeFixture.Task("task-high", childNodeId: "node-high"),
                BpmnRuntimeFixture.Task("task-low", childNodeId: "node-low"),
                BpmnRuntimeFixture.EndEvent("end-high"),
                BpmnRuntimeFixture.EndEvent("end-low")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "decision"),
                BpmnRuntimeFixture.Flow("flow-high", "decision", "task-high", conditionOutcome: "High"),
                BpmnRuntimeFixture.Flow("flow-low", "decision", "task-low", conditionOutcome: "Low"),
                BpmnRuntimeFixture.Flow("flow-2", "task-high", "end-high"),
                BpmnRuntimeFixture.Flow("flow-3", "task-low", "end-low")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertOutcomes("node-decision", "High");
        run.AssertRan("node-high");
        run.AssertDidNotRun("node-low");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BlankOutcome_FallsThroughToDefaultFlow()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-decision", "actexec-fallback");
        var executable = fixture.NewExecutable(
            children:
            [
                NewDecisionNode("node-decision", outcome: "  ", "High"),
                fixture.NewProbeNode("node-high"),
                fixture.NewProbeNode("node-fallback")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ExclusiveGateway("decision", childNodeId: "node-decision"),
                BpmnRuntimeFixture.Task("task-high", childNodeId: "node-high"),
                BpmnRuntimeFixture.Task("task-fallback", childNodeId: "node-fallback"),
                BpmnRuntimeFixture.EndEvent("end-high"),
                BpmnRuntimeFixture.EndEvent("end-fallback")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "decision"),
                BpmnRuntimeFixture.Flow("flow-high", "decision", "task-high", conditionOutcome: "High"),
                BpmnRuntimeFixture.Flow("flow-fallback", "decision", "task-fallback", isDefault: true),
                BpmnRuntimeFixture.Flow("flow-2", "task-high", "end-high"),
                BpmnRuntimeFixture.Flow("flow-3", "task-fallback", "end-fallback")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-fallback");
        run.AssertDidNotRun("node-high");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
    }
}
