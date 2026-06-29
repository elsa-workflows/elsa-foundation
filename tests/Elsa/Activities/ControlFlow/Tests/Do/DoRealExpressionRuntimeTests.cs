using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Activities.Do;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DoActivity = Elsa.Activities.Do.Activities.Do;

namespace Elsa.Activities.Do.Tests;

/// <summary>
/// End-to-end termination coverage for <c>Do</c> using a <b>real expression</b> (JavaScript via Jint),
/// not a counting mock. The body produces an activity output that persists as a durable value and
/// re-projects into materialization each pass; the loop condition is a real JS expression over that output.
/// This proves the loop genuinely observes per-pass state the body changed — the counting-mock runtime
/// tests cannot distinguish real observation from a hidden counter, so this locks the supported path.
///
/// It also covers the post-test contract (the body runs at least once even when the condition is false on
/// entry), the <c>Break</c> early-exit, and documents the #286 boundary: a condition over a
/// <b>workflow-scope</b> variable the body mutates does NOT yet terminate (workflow variables are seeded
/// at start only, with no mid-run write-back), so a bounded run still reports the loop never completing.
/// </summary>
public sealed class DoRealExpressionRuntimeTests
{
    private const string Int32TypeName = "System.Int32";
    private const string BooleanTypeName = "System.Boolean";

    [Fact]
    public async Task ActivityOutputCondition_TerminatesAfterTheBodyDrivesItFalse()
    {
        // Body increments and emits output `count`; the JS condition `output.count < 3` re-reads the latest
        // persisted output after each pass. The body runs once unconditionally then repeats while the
        // condition holds, so it runs exactly 3 times (count reaches 3) and the loop completes.
        var bodyIds = Enumerable.Range(0, 3).Select(i => $"actexec-body-{i}").ToArray();
        await using var harness = NewHarness("actexec-do", bodyIds);

        var run = await harness.RunAsync(NewOutputDrivenExecutable(limit: 3));

        Assert.Equal(3, run.States("node-body").Count);
        run.AssertOutcomes("node-do", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BodyRunsOnce_EvenWhenConditionIsFalseOnEntry()
    {
        // Condition `output.count < 1`: after the unconditional first pass the count is 1, so 1 < 1 is
        // false and the loop completes — but the body has still run exactly once (post-test semantics).
        var bodyIds = Enumerable.Range(0, 1).Select(i => $"actexec-body-{i}").ToArray();
        await using var harness = NewHarness("actexec-do", bodyIds);

        var run = await harness.RunAsync(NewOutputDrivenExecutable(limit: 1));

        Assert.Single(run.States("node-body"));
        run.AssertOutcomes("node-do", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BodyBreakOutcome_EndsLoopEarly_BeforeTheConditionWouldStop()
    {
        // The condition (`output.count < 5`) would allow 5 passes, but the body emits a Break outcome once
        // its count reaches 2, so the loop ends early after exactly 2 passes.
        var bodyIds = Enumerable.Range(0, 2).Select(i => $"actexec-body-{i}").ToArray();
        await using var harness = NewHarness("actexec-do", bodyIds);

        var run = await harness.RunAsync(NewOutputDrivenExecutable(limit: 5, breakAt: 2));

        Assert.Equal(2, run.States("node-body").Count);
        run.AssertOutcomes("node-do", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task WorkflowScopeVariableCondition_DoesNotYetTerminate_DocumentingThe286Limitation()
    {
        // The body writes a workflow-scope variable, but Seam C seeds workflow variables at start only with
        // no mid-run write-back (#286). The JS condition `variables.counter < 3` keeps seeing the start-time
        // seed, so the loop runs every pass the id budget allows and never satisfies its exit condition. We
        // hand out a budget of 5 body passes — well past the limit of 3 a terminating loop would stop at —
        // and assert the budget is fully consumed and the workflow never reaches Completed, locking the
        // boundary until #286 (mid-run workflow-variable write-back) lands.
        const int budget = 5;
        const int limit = 3;
        var bodyIds = Enumerable.Range(0, budget).Select(i => $"actexec-body-{i}").ToArray();
        await using var harness = NewHarness("actexec-do", bodyIds);

        var run = await harness.RunAsync(NewWorkflowVariableDrivenExecutable(limit));

        // A terminating loop would stop at `limit` (3) passes; instead it consumes the whole budget ...
        Assert.Equal(budget, run.States("node-body").Count);
        Assert.True(run.States("node-body").Count > limit, "workflow-scope condition unexpectedly terminated at the limit");
        // ... and the composite/workflow never completes, because the condition never observes the change.
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    private static WorkflowExecutionHarness NewHarness(string compositeId, params string[][] bodyIdGroups)
    {
        var ids = new List<string> { compositeId };
        foreach (var group in bodyIdGroups)
            ids.AddRange(group);
        return BuildHarness(ids);
    }

    private static WorkflowExecutionHarness BuildHarness(IEnumerable<string> activityExecutionIds)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<DoActivityConstructor>()
            .WithConstructor<CounterBodyConstructor>()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
                // Surfaces variables/inputs/outputs to the engine at materialization time (self-contained).
                services.AddScoped<IScriptPreProcessor, MaterializationAccessorsPreProcessor>();
            })
            .Build(activityExecutionIds);

        RunStartupTasks(harness.Services);
        return harness;
    }

    private static void RunStartupTasks(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
            task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Do(condition = JS `output.count &lt; limit`) over a body that increments and emits output `count`.
    /// The body's `Next` input is the prior output plus one (JS `(output.count == null ? 0 : output.count) + 1`),
    /// so each pass persists a new durable `count` the condition re-reads. When <paramref name="breakAt"/>
    /// is set, the body emits a <c>Break</c> outcome once its count reaches that value.
    /// </summary>
    private static WorkflowExecutable NewOutputDrivenExecutable(int limit, int? breakAt = null)
    {
        var body = new ExecutableNode(
            executableNodeId: "node-body",
            authoredActivityId: "authored-body",
            activityType: CounterBody.ActivityTypeName,
            activityTypeVersion: "1.0.0",
            descriptorType: CounterBodyConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new CounterBodyDescriptor(BreakAt: breakAt)),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Next"] = JavaScriptInt("(output.count == null ? 0 : output.count) + 1")
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                ["count"] = new RuntimeOutputCapture(
                    outputName: "count",
                    valueId: "do-count",
                    type: new RuntimeValueTypeDescriptor("clr", Int32TypeName, null),
                    lifecycle: DurableValueLifecycle.Instance,
                    storage: DurableValueStorage.Inline,
                    captureOnSuccessfulCompletion: true)
            },
            metadata: new Dictionary<string, string>());

        return NewDoExecutable(
            conditionBinding: JavaScriptBool($"output.count == null ? true : output.count < {limit}"),
            body: body);
    }

    /// <summary>
    /// Do(condition = JS `variables.counter &lt; limit`) over a body that mutates the workflow-scope
    /// variable. Because Seam C has no mid-run write-back (#286), the condition never observes the change.
    /// </summary>
    private static WorkflowExecutable NewWorkflowVariableDrivenExecutable(int limit)
    {
        var body = new ExecutableNode(
            executableNodeId: "node-body",
            authoredActivityId: "authored-body",
            activityType: CounterBody.ActivityTypeName,
            activityTypeVersion: "1.0.0",
            descriptorType: CounterBodyConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new CounterBodyDescriptor("counter")),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Next"] = JavaScriptInt("(variables.counter == null ? 0 : variables.counter) + 1")
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return NewDoExecutable(
            conditionBinding: JavaScriptBool($"variables.counter == null ? true : variables.counter < {limit}"),
            body: body,
            workflowVariableName: "counter");
    }

    private static WorkflowExecutable NewDoExecutable(
        RuntimeInputBinding conditionBinding,
        ExecutableNode body,
        string? workflowVariableName = null)
    {
        object structurePayload = workflowVariableName is null
            ? new { body = "node-body" }
            : new
            {
                body = "node-body",
                variables = new[] { new { referenceKey = $"var-{workflowVariableName}", name = workflowVariableName, @default = new { value = 0, type = "Literal" } } }
            };

        var root = new ExecutableNode(
            executableNodeId: "node-do",
            authoredActivityId: "authored-do",
            activityType: typeof(DoActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: DoActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new DoDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["Condition"] = conditionBinding },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(DoActivity.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(
                DoActivity.StructureKind,
                DoActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structurePayload)));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static RuntimeInputBinding JavaScriptInt(string expression) =>
        JavaScript("Next", expression, Int32TypeName);

    private static RuntimeInputBinding JavaScriptBool(string expression) =>
        JavaScript("Condition", expression, BooleanTypeName);

    private static RuntimeInputBinding JavaScript(string inputName, string expression, string typeName) =>
        new(
            inputName: inputName,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", expression, new RuntimeValueTypeDescriptor("clr", typeName, null)),
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = typeName,
                ["referenceKey"] = inputName
            });

    /// <summary>
    /// Body activity: emits its <c>Next</c> input as output <c>count</c>, driving the loop condition. When
    /// <c>BreakAt</c> is set, it emits a <c>Break</c> outcome once the value reaches that threshold.
    /// </summary>
    private sealed class CounterBody(InputArgument<int> next, OutputArgument<object?>? count, int? breakAt) : CodeActivity(ActivityTypeName)
    {
        public const string ActivityTypeName = "test/counter-body";

        protected override void Execute(IActivityExecutionContext context)
        {
            var value = context.Get(next);
            if (count is not null)
                context.Set(count, value, "count");
            if (breakAt is { } threshold && value >= threshold)
                context.SetOutcomes([DoActivity.BreakOutcome]);
        }
    }

    private sealed record CounterBodyDescriptor(string? OutputName = "count", int? BreakAt = null);

    private sealed class CounterBodyConstructor : IActivityConstructor<CounterBodyDescriptor>
    {
        public static string DescriptorTypeKey => typeof(CounterBodyDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            Construct(payload.Deserialize<CounterBodyDescriptor>() ?? new CounterBodyDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(
            CounterBodyDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            if (inputs is null || !inputs.TryGetValue("Next", out var nextInput))
                throw new InvalidOperationException("CounterBody expected a materialized 'Next' input argument.");

            OutputArgument<object?>? countOutput = null;
            if (outputs is not null && outputs.TryGetValue("count", out var output))
                countOutput = (OutputArgument<object?>)output;

            return new(new CounterBody((InputArgument<int>)nextInput, countOutput, descriptor.BreakAt));
        }
    }

    private sealed class DoActivityConstructor : IActivityConstructor<DoDescriptor>
    {
        public static string DescriptorTypeKey => typeof(DoDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

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
