using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Activities.Do;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DoActivity = Elsa.Activities.Do.Activities.Do;

namespace Elsa.Activities.Do.Tests;

/// <summary>
/// In-process execution coverage for the <c>Do</c> post-test composite running through the real workflow
/// agent. Built on the shared <see cref="WorkflowExecutionHarness"/>: this file declares the Do-specific
/// activity constructor and graph shape plus a counting condition evaluator; provider wiring, execution,
/// and assertions come from the harness.
///
/// The condition is bound as an expression so the runtime re-materializes it for every evaluation. A
/// <see cref="CountingConditionEvaluator"/> returns <c>true</c> for its first <c>holdEvaluations</c>
/// evaluations and <c>false</c> afterwards. The composite's inputs are materialized once on entry (that
/// evaluation is consumed but, by the post-test contract, ignored — <c>Do</c> schedules the first pass
/// unconditionally) and again after each completed pass. So the body runs <c>max(1, holdEvaluations)</c>
/// times: at least once, then once more per post-completion evaluation that still holds.
///
/// These tests exercise the loop's <em>scheduling and re-evaluation cadence</em> (body runs at least once,
/// re-evaluation per completed pass, distinct iteration id per pass, completion when the condition drops).
/// They deliberately do not prove that a real condition observes per-pass state the body changed — a
/// counting mock cannot distinguish real observation from a hidden counter. That end-to-end termination
/// guarantee is covered by <see cref="DoRealExpressionRuntimeTests"/> using a real JavaScript condition
/// over a persisted body output.
/// </summary>
public sealed class DoRuntimeTests
{
    [Theory]
    [InlineData(0, 1)] // condition false from the start: the unconditional first pass still runs the body once.
    [InlineData(1, 1)] // entry evaluation is consumed/ignored; the first post-completion evaluation is already false.
    [InlineData(3, 3)]
    public async Task ConditionThatHoldsForNEvaluations_RunsBodyExpectedTimes_AndCompletes(int holdEvaluations, int expectedPasses)
    {
        var activityExecutionIds = ActivityExecutionIds("actexec-do", "actexec-body", expectedPasses);
        await using var harness = NewHarness(holdEvaluations, activityExecutionIds);

        var run = await harness.RunAsync(NewExecutable());

        Assert.Equal(expectedPasses, run.States("node-body").Count);
        run.AssertOutcomes("node-do", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BodyRunsAtLeastOnce_EvenWhenConditionFalseFromTheStart()
    {
        // The counting evaluator returns false on its very first evaluation; the body still runs once
        // because the first pass is unconditional (post-test semantics).
        var activityExecutionIds = ActivityExecutionIds("actexec-do", "actexec-body", passes: 1);
        await using var harness = NewHarness(holdEvaluations: 0, activityExecutionIds);

        var run = await harness.RunAsync(NewExecutable());

        Assert.Single(run.States("node-body"));
        run.AssertOutcomes("node-do", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task EachPass_RecordsADistinctIterationId()
    {
        const int holdEvaluations = 3; // 3 passes total.
        const int expectedPasses = 3;
        var activityExecutionIds = ActivityExecutionIds("actexec-do", "actexec-body", expectedPasses);
        await using var harness = NewHarness(holdEvaluations, activityExecutionIds);

        var run = await harness.RunAsync(NewExecutable());

        var iterationIds = run.States("node-body").Select(state => state.IterationId).ToList();
        Assert.Equal(expectedPasses, iterationIds.Count);
        Assert.All(iterationIds, id => Assert.False(string.IsNullOrEmpty(id)));
        // Per-iteration state is distinct: no two passes share an iteration id.
        Assert.Equal(expectedPasses, iterationIds.Distinct().Count());
    }

    [Fact]
    public async Task BodyBreak_EndsLoopEarly_EvenWhileTheConditionStillHolds()
    {
        // The condition would hold for three passes, but the body breaks on its first pass: exactly one
        // body pass (< 3) runs and the loop completes early, without re-checking the condition.
        await using var harness = NewHarness(holdEvaluations: 3, "actexec-do", "actexec-body-0");

        var run = await harness.RunAsync(NewExecutable(breakOnEntry: true));

        Assert.Single(run.States("node-body"));
        run.AssertOutcomes("node-do", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    private static string[] ActivityExecutionIds(string composite, string bodyPrefix, int passes)
    {
        var ids = new List<string> { composite };
        for (var i = 0; i < passes; i++)
            ids.Add($"{bodyPrefix}-{i}");
        return ids.ToArray();
    }

    private static WorkflowExecutionHarness NewHarness(int holdEvaluations, params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<DoActivityConstructor>()
            .WithProbeLeaf()
            .ConfigureServices(services => services.AddSingleton<IExpressionEvaluator>(new CountingConditionEvaluator(holdEvaluations)))
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(bool breakOnEntry = false)
    {
        // A body that completes with the Break outcome models a Break leaf placed in the loop body (#299);
        // the loop recognizes it by name and ends early instead of re-checking the condition.
        var bodyOutcomes = breakOnEntry ? new[] { ActivityOutcomes.Break } : null;
        var root = new ExecutableNode(
            executableNodeId: "node-do",
            authoredActivityId: "authored-do",
            activityType: typeof(DoActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(DoActivityConstructor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new DoDescriptor())),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = new RuntimeInputBinding(
                    inputName: "Condition",
                    source: RuntimeInputBindingSource.Expression,
                    expression: new RuntimeExpressionBinding(CountingConditionEvaluator.Language, "do-condition"),
                    metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.Boolean" })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(DoActivity.BodySlotName, [WorkflowExecutionHarness.NewProbeNode("node-body", bodyOutcomes)])],
            structure: new ExecutableActivityStructure(
                DoActivity.StructureKind,
                DoActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = "node-body" })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    /// <summary>
    /// Deterministic condition evaluator that returns <c>true</c> for its first <c>holdEvaluations</c>
    /// evaluations and <c>false</c> thereafter. The composite's inputs are materialized once on entry
    /// (consumed but ignored by the post-test contract) and again after each completed pass, so a
    /// <c>Do</c> bound to it runs its body <c>max(1, holdEvaluations)</c> times.
    /// </summary>
    private sealed class CountingConditionEvaluator(int holdEvaluations) : IExpressionEvaluator
    {
        public const string Language = "test/counting-condition";

        private int _evaluations;

        public ValueTask<T?> EvaluateAsync<T>(IExpression expression, IExpressionExecutionContext context, IExpressionEvaluatorOptions? options = default) =>
            new((T?)Evaluate());

        public ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions? options = default) =>
            new(Evaluate());

        private object Evaluate()
        {
            var hold = _evaluations < holdEvaluations;
            _evaluations++;
            return hold;
        }
    }

    private sealed class DoActivityConstructor : IActivityConstructor<DoDescriptor>
    {
        public static string ConsumerKeyValue => typeof(DoDescriptor).FullName!;
        public string ConsumerKey => ConsumerKeyValue;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            Construct(new DoDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(
            DoDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var activity = new DoActivity();
            if (inputs is not null && inputs.TryGetValue("Condition", out var conditionInput))
                activity.Condition = (InputArgument<bool>)conditionInput;
            return new(activity);
        }
    }

    private sealed record DoDescriptor;
}
