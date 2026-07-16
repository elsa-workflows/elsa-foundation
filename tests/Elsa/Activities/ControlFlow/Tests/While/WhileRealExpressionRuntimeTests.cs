using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Activities.While;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.While.Tests;

/// <summary>
/// End-to-end termination coverage for <c>While</c> using a <b>real expression</b> (JavaScript via Jint),
/// not a counting mock. The body produces an activity output that persists as a durable value and
/// re-projects into materialization each pass; the loop condition is a real JS expression over that output.
/// This proves the loop genuinely observes per-pass state the body changed — the counting-mock runtime
/// tests cannot distinguish real observation from a hidden counter, so this locks the supported path.
///
/// It also proves workflow state advanced by an explicit graph-visible <c>Set</c> intrinsic. The condition
/// receives the variable through a declared portable-expression parameter; there is no ambient mutation.
/// </summary>
public sealed class WhileRealExpressionRuntimeTests
{
    private const string Int32TypeName = "System.Int32";
    private const string BooleanTypeName = "System.Boolean";

    [Fact]
    public async Task ActivityOutputCondition_TerminatesAfterTheBodyDrivesItFalse()
    {
        // Body increments and emits output `count`; the JS condition `output.count < 3` re-reads the latest
        // persisted output each pass, so the body runs exactly 3 times and the loop completes.
        var bodyIds = Enumerable.Range(0, 3).Select(i => $"actexec-body-{i}").ToArray();
        await using var harness = NewHarness("actexec-while", bodyIds);

        var run = await harness.RunAsync(NewOutputDrivenExecutable(limit: 3));

        Assert.Equal(3, run.States("node-body").Count);
        run.AssertOutcomes("node-while", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task WorkflowScopeVariableCondition_TerminatesThroughExplicitSetIntrinsic()
    {
        // The explicit Set body increments the root-frame variable. The condition reads only the declared
        // parameter, so a five-pass budget proves it stops at the actual limit of three.
        const int budget = 5;
        const int limit = 3;
        var bodyIds = Enumerable.Range(0, budget).Select(i => $"actexec-body-{i}").ToArray();
        await using var harness = NewHarness("actexec-while", bodyIds);

        var run = await harness.RunAsync(NewWorkflowVariableDrivenExecutable(limit));

        // The loop terminates at the limit (3 passes), not the full budget (5), because the condition now sees
        // the mutated workflow-scope variable.
        Assert.Equal(limit, run.States("node-body").Count);
        run.AssertOutcomes("node-while", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
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
            .WithConstructor<WhileActivityConstructor>()
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
    /// While(condition = JS `output.count &lt; limit`) over a body that increments and emits output `count`.
    /// The body's `Next` input is the prior output plus one (JS `(output.count == null ? 0 : output.count) + 1`),
    /// so each pass persists a new durable `count` the condition re-reads.
    /// </summary>
    private static WorkflowExecutable NewOutputDrivenExecutable(int limit)
    {
        var body = new ExecutableNode(
            executableNodeId: "node-body",
            authoredActivityId: "authored-body",
            activityType: CounterBody.ActivityTypeName,
            activityTypeVersion: "1.0.0",
            descriptorType: CounterBodyConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new CounterBodyDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Next"] = JavaScriptInt("(output.count == null ? 0 : output.count) + 1")
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                ["count"] = new RuntimeOutputCapture(
                    outputName: "count",
                    valueId: "while-count",
                    type: new RuntimeValueTypeDescriptor("clr", Int32TypeName, null),
                    lifecycle: DurableValueLifecycle.Instance,
                    storage: DurableValueStorage.Inline,
                    captureOnSuccessfulCompletion: true)
            },
            metadata: new Dictionary<string, string>());

        return NewWhileExecutable(
            conditionBinding: JavaScriptBool($"output.count == null ? true : output.count < {limit}"),
            body: body);
    }

    /// <summary>
    /// While(condition = portable JS `args.counter &lt; limit`) over a graph-visible <c>Set</c> intrinsic.
    /// </summary>
    private static WorkflowExecutable NewWorkflowVariableDrivenExecutable(int limit)
    {
        var body = NewCounterSetIntrinsic();

        return NewWhileExecutable(
            conditionBinding: PortableCounterExpression("Condition", $"args.counter < {limit}", "Boolean"),
            body: body,
            workflowVariableName: "counter");
    }

    private static WorkflowExecutable NewWhileExecutable(
        RuntimeInputBinding conditionBinding,
        ExecutableNode body,
        string? workflowVariableName = null)
    {
        // Direct executable fixtures carry canonical Runtime variable declarations. Publishing performs
        // this lowering for authored Sequence/Flowchart variables; a null name declares no root variable.
        object structurePayload = workflowVariableName is null
            ? new { body = "node-body" }
            : new
            {
                body = "node-body",
                variables = new[]
                {
                    VariableDeclaration($"var-{workflowVariableName}", workflowVariableName, 0)
                }
            };

        var root = new ExecutableNode(
            executableNodeId: "node-while",
            authoredActivityId: "authored-while",
            activityType: typeof(WhileActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WhileActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new WhileDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["Condition"] = conditionBinding },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(WhileActivity.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(
                WhileActivity.StructureKind,
                WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structurePayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static RuntimeVariableDeclaration VariableDeclaration(string key, string name, int value)
    {
        var type = new ValueTypeDescriptor("Int32");
        var policy = ValueProtectionPolicy.InstanceInline;
        var envelope = ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), policy);
        return new RuntimeVariableDeclaration(
            key,
            name,
            type,
            policy,
            new RuntimeInputBinding(key, type, policy, RuntimeInputBindingSource.Literal, literal: envelope));
    }

    private static RuntimeInputBinding JavaScriptInt(string expression) =>
        JavaScript("Next", expression, Int32TypeName);

    private static RuntimeInputBinding JavaScriptBool(string expression) =>
        JavaScript("Condition", expression, BooleanTypeName);

    private static ExecutableNode NewCounterSetIntrinsic() =>
        new(
            executableNodeId: "node-body",
            authoredActivityId: "authored-body",
            activityType: "elsa.intrinsic.set",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Value] = PortableCounterExpression(
                    WorkflowIntrinsicInputKeys.Value,
                    "args.counter + 1",
                    "Int32")
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference("var-counter", VariableReference.WorkflowScopeId));

    private static RuntimeInputBinding PortableCounterExpression(string inputName, string expression, string targetType) =>
        new(
            inputKey: inputName,
            targetType: new ValueTypeDescriptor(targetType),
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "JavaScript",
                expression,
                new RuntimeValueTypeDescriptor("alias", targetType, null),
                parameters: new Dictionary<string, ExpressionParameterBinding>
                {
                    ["counter"] = new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, "var-counter")
                },
                options: JsonSerializer.SerializeToElement(new { }),
                capabilityProfile: ExpressionCapabilityProfiles.BindingPureV1));

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
    /// Body activity for the output-driven scenario: emits its incremented <c>Next</c> input as
    /// output <c>count</c>. Variable-driven scenarios use the engine <c>Set</c> intrinsic instead.
    /// </summary>
    private sealed class CounterBody(InputArgument<int> next, OutputArgument<object?>? count) : CodeActivity(ActivityTypeName)
    {
        public const string ActivityTypeName = "test/counter-body";

        protected override void Execute(IActivityExecutionContext context)
        {
            var value = context.Get(next);
            if (count is not null)
                context.Set(count, value, "count");
        }
    }

    private sealed record CounterBodyDescriptor(string? OutputName = "count");

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

            return new(new CounterBody((InputArgument<int>)nextInput, countOutput));
        }
    }

    private sealed class WhileActivityConstructor : IActivityConstructor<WhileDescriptor>
    {
        public static string DescriptorTypeKey => typeof(WhileDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

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
            => new(new WhileActivity());
    }

    private sealed record WhileDescriptor;
}
