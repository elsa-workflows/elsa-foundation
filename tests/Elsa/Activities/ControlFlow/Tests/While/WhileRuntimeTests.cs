using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Activities.While;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.While.Tests;

/// <summary>
/// In-process execution coverage for the <c>While</c> composite running through the real workflow agent.
/// Built on the shared <see cref="WorkflowExecutionHarness"/>: this file declares the While-specific
/// activity constructor and graph shape plus a counting condition evaluator; provider wiring, execution,
/// and assertions come from the harness.
///
/// The condition is bound as an expression so the runtime re-materializes it for every child-completion
/// evaluation (the parent composite's inputs are rebuilt each pass). A <see cref="CountingConditionEvaluator"/>
/// returns <c>true</c> for the first N evaluations and <c>false</c> afterwards, so the body runs exactly N
/// times before the loop completes — modelling a condition that flips after N passes.
///
/// These tests exercise the loop's <em>scheduling and re-evaluation cadence</em> (one condition evaluation
/// per pass, distinct iteration id per pass, completion when the condition drops). They deliberately do
/// not prove that a real condition observes per-pass state the body changed — a counting mock cannot
/// distinguish real observation from a hidden counter. That end-to-end termination guarantee is covered by
/// <see cref="WhileRealExpressionRuntimeTests"/> using a real JavaScript condition over a persisted body
/// output.
/// </summary>
public sealed class WhileRuntimeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ConditionThatFlipsAfterNPasses_RunsBodyExactlyNTimes_AndCompletes(int passes)
    {
        // Activity-execution ids: the composite plus one per body pass.
        var activityExecutionIds = ActivityExecutionIds("actexec-while", "actexec-body", passes);
        await using var harness = NewHarness(passes, activityExecutionIds);

        var run = await harness.RunAsync(NewExecutable());

        Assert.Equal(passes, run.States("node-body").Count);
        run.AssertOutcomes("node-while", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task ConditionFalseOnEntry_NeverRunsBody_AndCompletes()
    {
        // Zero passes: the condition is false on the entry evaluation, so the body never runs.
        await using var harness = NewHarness(passes: 0, "actexec-while");

        var run = await harness.RunAsync(NewExecutable());

        run.AssertDidNotRun("node-body");
        run.AssertOutcomes("node-while", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task EachPass_RecordsADistinctIterationId()
    {
        const int passes = 3;
        var activityExecutionIds = ActivityExecutionIds("actexec-while", "actexec-body", passes);
        await using var harness = NewHarness(passes, activityExecutionIds);

        var run = await harness.RunAsync(NewExecutable());

        var iterationIds = run.States("node-body").Select(state => state.IterationId).ToList();
        Assert.Equal(passes, iterationIds.Count);
        Assert.All(iterationIds, id => Assert.False(string.IsNullOrEmpty(id)));
        // Per-iteration state is distinct: no two passes share an iteration id.
        Assert.Equal(passes, iterationIds.Distinct().Count());
    }

    [Fact]
    public async Task BodyBreak_EndsLoopEarly_EvenWhileTheConditionStillHolds()
    {
        // The condition would hold for three passes, but the body breaks on its first pass: exactly one
        // body pass (< 3) runs and the loop completes early, before the condition is re-evaluated.
        await using var harness = NewHarness(passes: 3, "actexec-while", "actexec-body-0");

        var run = await harness.RunAsync(NewExecutable(breakOnEntry: true));

        Assert.Single(run.States("node-body"));
        run.AssertOutcomes("node-while", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    private static string[] ActivityExecutionIds(string composite, string bodyPrefix, int passes)
    {
        var ids = new List<string> { composite };
        for (var i = 0; i < passes; i++)
            ids.Add($"{bodyPrefix}-{i}");
        return ids.ToArray();
    }

    private static WorkflowExecutionHarness NewHarness(int passes, params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<WhileActivityConstructor>()
            .WithProbeLeaf()
            .ConfigureServices(services => services.AddSingleton<IExpressionEvaluator>(new CountingConditionEvaluator(passes)))
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(bool breakOnEntry = false)
    {
        // A body that completes with the Break outcome models a Break leaf placed in the loop body (#299);
        // the loop recognizes it by name and ends early instead of re-evaluating the condition.
        var bodyOutcomes = breakOnEntry ? new[] { ActivityOutcomes.Break } : null;
        var root = new ExecutableNode(
            executableNodeId: "node-while",
            authoredActivityId: "authored-while",
            activityType: typeof(WhileActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(WhileActivityConstructor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new WhileDescriptor())),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = new RuntimeInputBinding(
                    inputName: "Condition",
                    source: RuntimeInputBindingSource.Expression,
                    expression: new RuntimeExpressionBinding(CountingConditionEvaluator.Language, "while-condition"),
                    metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.Boolean" })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(WhileActivity.BodySlotName, [WorkflowExecutionHarness.NewProbeNode("node-body", bodyOutcomes)])],
            structure: new ExecutableActivityStructure(
                WhileActivity.StructureKind,
                WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = "node-body" })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    /// <summary>
    /// Deterministic condition evaluator that returns <c>true</c> for its first <c>passes</c> evaluations
    /// and <c>false</c> thereafter, modelling a boolean condition that flips after N passes without relying
    /// on a scripting language. Each input materialization triggers exactly one evaluation, so a
    /// <c>While</c> bound to it runs its body exactly <c>passes</c> times.
    /// </summary>
    private sealed class CountingConditionEvaluator(int passes) : IExpressionEvaluator
    {
        public const string Language = "test/counting-condition";

        private int _evaluations;

        public ValueTask<T?> EvaluateAsync<T>(IExpression expression, IExpressionExecutionContext context, IExpressionEvaluatorOptions? options = default) =>
            new((T?)Evaluate());

        public ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions? options = default) =>
            new(Evaluate());

        private object Evaluate()
        {
            var hold = _evaluations < passes;
            _evaluations++;
            return hold;
        }
    }

    private sealed class WhileActivityConstructor : IActivityConstructor<WhileDescriptor>
    {
        public static string ConsumerKeyValue => typeof(WhileDescriptor).FullName!;
        public string ConsumerKey => ConsumerKeyValue;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            Construct(new WhileDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(
            WhileDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var activity = new WhileActivity();
            if (inputs is not null && inputs.TryGetValue("Condition", out var conditionInput))
                activity.Condition = (InputArgument<bool>)conditionInput;
            return new(activity);
        }
    }

    private sealed record WhileDescriptor;
}
