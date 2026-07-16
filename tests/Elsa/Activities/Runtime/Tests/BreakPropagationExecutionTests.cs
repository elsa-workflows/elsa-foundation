using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;
using WhileActivity = Elsa.Activities.While.Activities.While;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using IfActivity = Elsa.Activities.If.Activities.If;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// End-to-end coverage for <c>Break</c> propagation through the composites a loop body commonly uses
/// (#299). The realistic shapes from the issue follow-up: a <c>Break</c> nested inside a <c>Sequence</c> or
/// an <c>If</c> branch must bubble out of that composite and end the enclosing loop. Every activity here —
/// the loops, the intermediate composites, and the <c>Break</c> leaf — is constructed by the production
/// transient CLR activation from its <see cref="ClrActivityDescriptor"/>
/// (stable-alias) descriptor, so the test exercises the real construct → bind → execute → propagate path rather than test
/// stubs. Marker steps are probe leaves so the test can assert which steps ran.
/// </summary>
public sealed class BreakPropagationExecutionTests
{
    [Fact]
    public async Task For_SequenceBodyWithBreakPartway_EndsLoopEarly_AndLaterStepNeverRuns()
    {
        // For [0,3) with a Sequence body of [stepA, Break, stepB]. The Sequence runs stepA, hits Break,
        // stops (stepB never runs) and completes with Break; the For sees that and ends after one pass — so
        // stepA runs once (not three times) and stepB never runs at all.
        await using var harness = NewHarness("actexec-for", "actexec-seq", "actexec-stepA", "actexec-break");

        var body = NewSequenceNode("node-seq",
        [
            WorkflowExecutionHarness.NewProbeNode("node-stepA"),
            NewClrLeafNode("node-break", typeof(Break)),
            WorkflowExecutionHarness.NewProbeNode("node-stepB")
        ]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewForNode("node-for", start: 0, end: 3, body: body)));

        Assert.Single(run.States("node-stepA")); // one pass only — the loop broke, it did not run 3 times.
        run.AssertDidNotRun("node-stepB");        // the step after Break in the Sequence never ran.
        run.AssertOutcomes("node-seq", ActivityOutcomes.Break); // Sequence bubbled Break up.
        run.AssertOutcomes("node-for", ActivityOutcomes.Done);  // For ended cleanly.
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task While_IfTrueBreakBody_EndsAfterOnePass()
    {
        // While(condition holds for 3 passes) with body If(true){ Break }. On the first pass the If takes
        // its Then branch, which Breaks; If completes with Break instead of True, the While sees it and ends
        // after a single pass rather than running three times.
        await using var harness = NewHarness(conditionPasses: 3, "actexec-while", "actexec-if", "actexec-break");

        var body = NewIfNode("node-if", condition: true, then: NewClrLeafNode("node-break", typeof(Break)));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewWhileNode("node-while", body)));

        Assert.Single(run.States("node-if")); // one pass only — the loop broke after the first If.
        run.AssertOutcomes("node-if", ActivityOutcomes.Break);  // If bubbled Break up instead of True.
        run.AssertOutcomes("node-while", ActivityOutcomes.Done); // While ended cleanly.
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task For_FlowchartBodyWithBreak_EndsLoopEarly_AndBubblesBreak()
    {
        // For [0,3) with a Flowchart body of node-a -> node-break. The Flowchart runs node-a, routes to the
        // Break leaf, which ends the Flowchart with a Break outcome (#304); the For sees that and ends after
        // one pass — so node-a runs once (not three times).
        await using var harness = NewHarness("actexec-for", "actexec-flowchart", "actexec-a", "actexec-break");

        var body = NewFlowchartNode("node-flowchart",
            children: [WorkflowExecutionHarness.NewProbeNode("node-a"), NewClrLeafNode("node-break", typeof(Break))],
            connections: [NewConnection("node-a", "node-break")],
            startNodeId: "node-a");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewForNode("node-for", start: 0, end: 3, body: body)));

        Assert.Single(run.States("node-a")); // one pass only — the loop broke, it did not run 3 times.
        run.AssertOutcomes("node-flowchart", ActivityOutcomes.Break); // Flowchart bubbled Break up.
        run.AssertOutcomes("node-for", ActivityOutcomes.Done);         // For ended cleanly.
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task For_FlowchartDiamondBodyWithBreakOnOneFork_EndsLoopEarly_AndJoinTargetNeverRuns()
    {
        // For [0,3) with a diamond Flowchart body: node-a forks to node-keep and node-break, both feeding the
        // implicit join node-d. The Break fork ends the whole Flowchart — the sibling fork's path and the
        // pending join are canceled — so the reconverged node-d after the join never runs and the For ends
        // after one pass. Covers the fork/join interaction: a Break short-circuits routing before the join.
        await using var harness = NewHarness("actexec-for", "actexec-flowchart", "actexec-a", "actexec-keep", "actexec-break");

        var body = NewFlowchartNode("node-flowchart",
            children:
            [
                WorkflowExecutionHarness.NewProbeNode("node-a"),
                WorkflowExecutionHarness.NewProbeNode("node-keep"),
                NewClrLeafNode("node-break", typeof(Break)),
                WorkflowExecutionHarness.NewProbeNode("node-d")
            ],
            connections:
            [
                NewConnection("node-a", "node-keep"),
                NewConnection("node-a", "node-break"),
                NewConnection("node-keep", "node-d"),
                NewConnection("node-break", "node-d")
            ],
            startNodeId: "node-a");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewForNode("node-for", start: 0, end: 3, body: body)));

        run.AssertDidNotRun("node-d"); // the join after the broken fork never fired, regardless of fork order.
        run.AssertOutcomes("node-flowchart", ActivityOutcomes.Break); // Flowchart bubbled Break up.
        run.AssertOutcomes("node-for", ActivityOutcomes.Done);         // For ended cleanly after one pass.
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        BaseHarness().Build(activityExecutionIds);

    private static WorkflowExecutionHarness NewHarness(int conditionPasses, params string[] activityExecutionIds) =>
        BaseHarness()
            .ConfigureServices(services => services.AddSingleton<Elsa.Expressions.Core.Contracts.IPortableExpressionEvaluator>(
                new CountingConditionEvaluator(conditionPasses)))
            .Build(activityExecutionIds);

    private static WorkflowExecutionHarness.Builder BaseHarness() =>
        WorkflowExecutionHarness.Create()
            // The transient CLR activator constructs every CLR
            // activity below from its ClrActivityDescriptor (stable-alias) descriptor; it depends on IPayloadSerializer from the
            // SerializationFeature. The probe leaf supplies the marker steps.
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            // ActivitiesFlowchartFeature wires the Flowchart execution engine and its policies for the
            // Flowchart-body Break cases (#304); harmless for the linear/branch cases above.
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithProbeLeaf();

    private static ExecutableNode NewForNode(string nodeId, int start, int end, ExecutableNode body) =>
        NewClrCompositeNode(
            nodeId,
            typeof(ForActivity),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Start"] = LiteralBinding("Start", start, "System.Int32"),
                ["End"] = LiteralBinding("End", end, "System.Int32")
            },
            childSlots: [new ExecutableChildSlot(ForActivity.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(
                ForActivity.StructureKind,
                ForActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));

    private static ExecutableNode NewWhileNode(string nodeId, ExecutableNode body) =>
        NewClrCompositeNode(
            nodeId,
            typeof(WhileActivity),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = new RuntimeInputBinding(
                    "Condition",
                    new ValueTypeDescriptor("Boolean"),
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Expression,
                    expression: new RuntimeExpressionBinding(
                        CountingConditionEvaluator.Language,
                        "while-condition",
                        new RuntimeValueTypeDescriptor("alias", "Boolean", null),
                        capabilityProfile: ExpressionCapabilityProfiles.BindingPureV1))
            },
            childSlots: [new ExecutableChildSlot(WhileActivity.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(
                WhileActivity.StructureKind,
                WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));

    private static ExecutableNode NewFlowchartNode(
        string nodeId,
        IReadOnlyList<ExecutableNode> children,
        IReadOnlyCollection<FlowchartConnection> connections,
        string startNodeId) =>
        NewClrCompositeNode(
            nodeId,
            typeof(FlowchartActivity),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(connections, startNodeId))));

    private static FlowchartConnection NewConnection(string sourceNodeId, string targetNodeId, string? sourcePort = null) =>
        new(new FlowchartEndpoint(sourceNodeId, sourcePort), new FlowchartEndpoint(targetNodeId));

    private static ExecutableNode NewSequenceNode(string nodeId, IReadOnlyList<ExecutableNode> activities) =>
        NewClrCompositeNode(
            nodeId,
            typeof(SequenceActivity),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, activities)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = activities.Select(a => a.ExecutableNodeId).ToArray() })));

    private static ExecutableNode NewIfNode(string nodeId, bool condition, ExecutableNode then) =>
        NewClrCompositeNode(
            nodeId,
            typeof(IfActivity),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = LiteralBinding("Condition", condition, "System.Boolean")
            },
            childSlots: [new ExecutableChildSlot(IfActivity.ThenSlotName, [then])],
            structure: new ExecutableActivityStructure(
                IfActivity.StructureKind,
                IfActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { then = then.ExecutableNodeId, @else = (string?)null })));

    private static ExecutableNode NewClrCompositeNode(
        string nodeId,
        Type activityType,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        IReadOnlyCollection<ExecutableChildSlot> childSlots,
        ExecutableActivityStructure structure) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(Serializer, activityType),
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: structure);

    private static ExecutableNode NewClrLeafNode(string nodeId, Type activityType) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(Serializer, activityType),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            activityContract: activityType == typeof(Break) ? BreakContract() : null);

    private static ActivityContract BreakContract() =>
        new(
            typeof(Break).FullName!,
            "1.0.0",
            ClrConstruction.DescriptorType,
            ClrConstruction.Payload(Serializer, typeof(Break)),
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(ActivityUnit))),
                true,
                ActivityValuePolicy.Default,
                []),
            [ActivityOutcomes.Break],
            new ActivityActivationRequirement(ClrConstruction.DescriptorType, TypeAliasConvention.CanonicalAlias(typeof(Break))));

    private static RuntimeInputBinding LiteralBinding(string inputName, object value, string typeName) =>
        CanonicalLiteral(inputName, typeName == "System.Int32" ? "Int32" : typeName == "System.Boolean" ? "Boolean" : "String", value);

    private static RuntimeInputBinding CanonicalLiteral(string inputName, string alias, object value)
    {
        var type = new ValueTypeDescriptor(alias);
        return new RuntimeInputBinding(
            inputName,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    private static IPayloadSerializer Serializer => new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    /// <summary>
    /// Deterministic condition evaluator that returns <c>true</c> for its first <c>passes</c> evaluations
    /// and <c>false</c> thereafter — so the <c>While</c> would run its body <c>passes</c> times if nothing
    /// broke out. Lets the Break test prove the loop ended early (one pass) despite the condition still
    /// holding.
    /// </summary>
    private sealed class CountingConditionEvaluator(int passes) : Elsa.Expressions.Core.Contracts.IPortableExpressionEvaluator
    {
        public const string Language = "test/counting-condition";

        private int _evaluations;

        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromResult(JsonSerializer.SerializeToElement(Evaluate()));

        private bool Evaluate()
        {
            var hold = _evaluations < passes;
            _evaluations++;
            return hold;
        }
    }
}
