using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using IfActivity = Elsa.Activities.If.Activities.If;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Section-1 acceptance gate (#269, parent PRD #255): a single workflow that <b>composes</b> the ported
/// control-flow, composite, and data-leaf activities — <c>If</c> + <c>ForEach</c> + <c>SetVariable</c> — and runs
/// to <see cref="WorkflowExecutionStatus.Completed"/> through the real
/// <see cref="WorkflowExecutionHarness"/> (real in-process agent + scheduler, real JavaScript expression
/// evaluation via Jint — no counting mock, no hand-built resolution context).
///
/// The composed shape proves the activities genuinely interoperate, not merely run in isolation:
/// <list type="number">
/// <item><c>SetVariable</c> seeds a workflow-scope counter (<c>count = 0</c>).</item>
/// <item><c>ForEach</c> iterates a three-item collection; on each pass the body is a <c>SetVariable</c> whose
/// value is the real JS expression <c>variables.count + 1</c> — so each iteration <b>reads the variable a prior
/// iteration wrote</b> and writes it back (variables flow across activities; loop bodies run; the keystone
/// write-back of #286 carries the mutation forward each pass).</item>
/// <item><c>If</c> branches on the real JS condition <c>variables.count === 3</c> — a condition over the variable
/// the loop produced — and its Then branch is a final <c>SetVariable</c> that records the outcome
/// (<c>result = "all-iterated"</c>), proving the branch evaluated and the loop's effect is observable downstream.</item>
/// </list>
/// </summary>
public sealed class ActivityLibraryAcceptanceTests
{
    private const string BooleanTypeName = "System.Boolean";
    private const string StringTypeName = "System.String";
    private const string ObjectTypeName = "System.Object";

    public static TheoryData<Type, Type, string[]> MigratedPrimitiveLeafContracts =>
        new()
        {
            { typeof(ReadLine), typeof(ReadLineResult), [] },
            { typeof(WriteLines), typeof(ActivityUnit), [nameof(WriteLines.Lines)] },
            { typeof(Event), typeof(EventResult), [nameof(Event.EventName), nameof(Event.CorrelationId)] }
        };

    [Theory]
    [MemberData(nameof(MigratedPrimitiveLeafContracts))]
    public void MigratedPrimitiveLeaf_UsesPlainInputsAndOneAtomicResult(
        Type activityType,
        Type expectedResultType,
        string[] expectedInputKeys)
    {
        var properties = activityType.GetProperties();
        var actualInputKeys = properties
            .Select(property => (Property: property, Attribute: property.GetCustomAttributes(typeof(ActivityInputAttribute), inherit: true).Cast<ActivityInputAttribute>().SingleOrDefault()))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate => candidate.Attribute!.Key ?? candidate.Property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedInputKeys.Order(StringComparer.Ordinal), actualInputKeys);
        Assert.DoesNotContain(properties, property => typeof(Argument).IsAssignableFrom(property.PropertyType));
        Assert.Equal(expectedResultType, FindActivityResultType(activityType));
    }

    private static Type? FindActivityResultType(Type activityType)
    {
        for (var current = activityType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Activity<>))
                return current.GetGenericArguments()[0];
        }

        return null;
    }

    [Fact]
    public async Task IfForEachSetVariable_ComposeAndRunToCompletion_ThroughTheRealHarness()
    {
        // Hand out a body-pass budget (5) comfortably beyond the collection size (3): a loop that failed to
        // observe its own per-pass mutation would not stop at the right count, and the If condition (== 3) would
        // not fire. Reaching the Then branch is therefore real proof the variable flowed across all three.
        await using var harness = NewHarness(
            "actexec-sequence",
            "actexec-seed",
            "actexec-foreach",
            "actexec-incr-0", "actexec-incr-1", "actexec-incr-2", "actexec-incr-3", "actexec-incr-4",
            "actexec-if",
            "actexec-set-result");

        var run = await harness.RunAsync(NewComposedExecutable(["a", "b", "c"]));

        // 1. The whole composition drains to Completed.
        run.AssertWorkflowCompleted();

        // 2. The ForEach body ran exactly once per item — the loop genuinely iterated the collection.
        Assert.Equal(3, run.States("node-incr").Count);

        // 3. The If evaluated its `variables.count === 3` condition to True (the branch the loop's effect selects)
        //    and ran its Then branch while leaving the Else branch unexecuted.
        run.AssertOutcomes("node-if", ActivityOutcomes.True);
        run.AssertRan("node-set-result");
        run.AssertDidNotRun("node-else");

        // 4. The final persisted workflow-scope variables reflect the loop's effect end-to-end: the counter the
        //    ForEach body accumulated reads back as 3, and the Then-branch SetVariable recorded the outcome.
        var durableValues = await harness.Services.GetRequiredService<IDurableValueStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Equal(3, VariableInt(durableValues, "count"));
        Assert.Equal("all-iterated", VariableString(durableValues, "result"));
    }

    [Fact]
    public async Task IfForEachSetVariable_ComposeAndRunToCompletion_ThroughTheServerRequestHandlerPath()
    {
        // Server / end-to-end variant: the same composed If + ForEach + SetVariable workflow is started through the
        // real ExecuteWorkflowRequestHandler → IWorkflowStartDispatcher → scheduler-drain path (the same
        // path SeededVariableEndToEndExecutionTests exercises), not the harness's direct agent enqueue. A true
        // HTTP-server run is not feasible in this unit-test project, so this uses the request-handler path — the
        // server-side entrypoint — and asserts the run reaches Completed with the loop's effect observable in the
        // persisted workflow-scope variables.
        await using var provider = BuildEndToEndProvider();

        var executable = WrapWithIdentity(NewComposedRoot(["a", "b", "c"]), artifactId: "acceptance-e2e");
        await PublishedExecutableSeeder.SaveAsync(provider, executable);

        var handler = new Elsa.Workflows.Runtime.Api.Handlers.ExecuteWorkflowRequestHandler(
            provider.GetRequiredService<IWorkflowStartDispatcher>(),
            provider.GetRequiredService<IWorkflowExecutableStore>());

        var view = await handler.Handle(new Elsa.Workflows.Runtime.Api.Requests.ExecuteWorkflow(executable.Identity.ArtifactId), CancellationToken.None);

        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(view.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);

        // The loop's effect is observable end-to-end through the server path: the ForEach-accumulated counter reads
        // back as 3 and the If Then-branch SetVariable recorded the outcome.
        var durableValues = await provider.GetRequiredService<IDurableValueStateStore>().ListAsync(view.WorkflowExecutionId);
        Assert.Equal(3, VariableInt(durableValues, "count"));
        Assert.Equal("all-iterated", VariableString(durableValues, "result"));
    }

    private static ServiceProvider BuildEndToEndProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IActivityConstructor, SequenceConstructor>();
        services.AddSingleton<IActivityConstructor, ForEachConstructor>();
        services.AddSingleton<IActivityConstructor, IfConstructor>();
        services.AddSingleton<IActivityConstructor, SetVariableConstructor>();
        new EventsFeature().ConfigureServices(services);
        new SerializationFeature().ConfigureServices(services);
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        new Elsa.Workflows.Runtime.Api.WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        // Surfaces variables/inputs/outputs to the engine at materialization time (self-contained).
        services.AddScoped<IScriptPreProcessor, MaterializationAccessorsPreProcessor>();

        var provider = services.BuildServiceProvider();
        RunStartupTasks(provider);
        return provider;
    }

    private static WorkflowExecutable WrapWithIdentity(ExecutableNode root, string artifactId) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, $"def-{artifactId}", $"ver-{artifactId}", "1.0.0", $"sha256:{artifactId}"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero),
            compatibilityMetadata: new Dictionary<string, string>());

    private static int VariableInt(IEnumerable<DurableValueState> durableValues, string variableName) =>
        LatestVariable(durableValues, variableName).InlineValue!.Value.GetInt32();

    private static string? VariableString(IEnumerable<DurableValueState> durableValues, string variableName) =>
        LatestVariable(durableValues, variableName).InlineValue!.Value.GetString();

    private static DurableValueState LatestVariable(IEnumerable<DurableValueState> durableValues, string variableName) =>
        durableValues
            .Where(durable => durable.Metadata.TryGetValue(RuntimeMetadataKeys.VariableName, out var name) && name == variableName)
            .OrderBy(durable => durable.CapturedAt)
            .Last();

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<SequenceConstructor>()
            .WithConstructor<ForEachConstructor>()
            .WithConstructor<IfConstructor>()
            .WithConstructor<SetVariableConstructor>()
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
    /// Builds the composed executable: a Sequence root declaring workflow-scope variables <c>count</c> (Int32,
    /// default 0) and <c>result</c> (String, default "none"), whose children are the seed SetVariable, the
    /// ForEach loop, and the If branch.
    /// </summary>
    private static WorkflowExecutable NewComposedExecutable(IReadOnlyCollection<string> collection) =>
        WorkflowExecutionHarness.NewExecutable(NewComposedRoot(collection));

    private static ExecutableNode NewComposedRoot(IReadOnlyCollection<string> collection)
    {
        // (a) Seed the counter to 0 via SetVariable (literal). The value is bound as System.Object (the
        //     SetVariable.Value contract is InputArgument<object>), matching the production materialization.
        var seedNode = SetVariableNode(
            nodeId: "node-seed",
            variableName: "count",
            valueBinding: Literal("Value", JsonSerializer.SerializeToElement(0), ObjectTypeName));

        // (b) ForEach body: SetVariable count = `variables.count + 1` — each pass reads the variable a prior pass
        //     wrote (real JS arithmetic over the re-projected workflow-scope variable) and writes it back.
        var incrementNode = SetVariableNode(
            nodeId: "node-incr",
            variableName: "count",
            valueBinding: JavaScript("Value", "(variables.count == null ? 0 : variables.count) + 1", ObjectTypeName));

        var foreachNode = new ExecutableNode(
            executableNodeId: "node-foreach",
            authoredActivityId: "authored-foreach",
            activityType: typeof(ForEachActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ForEachConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new ForEachDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Collection"] = Literal("Collection", JsonSerializer.SerializeToElement(collection), ObjectTypeName)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(ForEachActivity.BodySlotName, [incrementNode])],
            structure: new ExecutableActivityStructure(
                ForEachActivity.StructureKind,
                ForEachActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = "node-incr" })));

        // (c) If on `variables.count === 3` — a condition over the variable the loop produced. Then branch records
        //     the outcome; Else branch is a probe that must not run.
        var setResultNode = SetVariableNode(
            nodeId: "node-set-result",
            variableName: "result",
            valueBinding: Literal("Value", JsonSerializer.SerializeToElement("all-iterated"), ObjectTypeName));

        var elseNode = WorkflowExecutionHarness.NewProbeNode("node-else");

        var ifNode = new ExecutableNode(
            executableNodeId: "node-if",
            authoredActivityId: "authored-if",
            activityType: typeof(IfActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: IfConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new IfDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = JavaScript("Condition", "variables.count === 3", BooleanTypeName)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot(IfActivity.ThenSlotName, [setResultNode]),
                new ExecutableChildSlot(IfActivity.ElseSlotName, [elseNode])
            ],
            structure: new ExecutableActivityStructure(
                IfActivity.StructureKind,
                IfActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { then = "node-set-result", @else = "node-else" })));

        var children = new[] { seedNode, foreachNode, ifNode };
        var structurePayload = new
        {
            activities = children.Select(child => child.ExecutableNodeId).ToArray(),
            variables = new[]
            {
                new VariableDefinition("var-count", "count", new TypeReference("Int32"), null, new ArgumentValue(0, "Literal")),
                new VariableDefinition("var-result", "result", new TypeReference("String"), null, new ArgumentValue("none", "Literal"))
            }
        };

        var root = new ExecutableNode(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: SequenceConstructor.SequenceDescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new SequenceDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structurePayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        return root;
    }

    private static ExecutableNode SetVariableNode(string nodeId, string variableName, RuntimeInputBinding valueBinding) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(SetVariable).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: SetVariableConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new SetVariableDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Variable"] = Literal("Variable", JsonSerializer.SerializeToElement(variableName), StringTypeName),
                ["Value"] = valueBinding
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding Literal(string inputName, JsonElement value, string typeName) =>
        new(
            inputName: inputName,
            source: RuntimeInputBindingSource.Literal,
            literalValue: value,
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = typeName,
                ["referenceKey"] = inputName
            });

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

    private sealed record SetVariableDescriptor
    {
        public static string DescriptorTypeKey => typeof(SetVariableDescriptor).FullName!;
    }

    private sealed class SetVariableConstructor : IActivityConstructor<SetVariableDescriptor>
    {
        public static string DescriptorTypeKey => SetVariableDescriptor.DescriptorTypeKey;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new SetVariableDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(SetVariableDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var activity = new SetVariable();
            if (inputs is not null && inputs.TryGetValue("Variable", out var variableInput))
                activity.Variable = (InputArgument<string>)variableInput;
            // This hand-rolled constructor casts the materialized argument directly, so it requires the Value
            // binding to be typed as System.Object (hence ObjectTypeName on every Value binding above). Typed
            // (Int32/String) Value bindings through the *real* ClrActivityConstructor + ActivityArgumentBinder are
            // covered by SetVariableTypedBindingTests (#313) — that path widens the argument to InputArgument<object>.
            if (inputs is not null && inputs.TryGetValue("Value", out var valueInput))
                activity.Value = (InputArgument<object>)valueInput;
            return new(activity);
        }
    }

    private sealed record ForEachDescriptor;

    private sealed class ForEachConstructor : IActivityConstructor<ForEachDescriptor>
    {
        public static string DescriptorTypeKey => typeof(ForEachDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new ForEachDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(ForEachDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var activity = new ForEachActivity();
            if (inputs is not null && inputs.TryGetValue("Collection", out var collectionInput))
                activity.Collection = (InputArgument<object>)collectionInput;
            return new(activity);
        }
    }

    private sealed record IfDescriptor;

    private sealed class IfConstructor : IActivityConstructor<IfDescriptor>
    {
        public static string DescriptorTypeKey => typeof(IfDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new IfDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(IfDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var activity = new IfActivity();
            if (inputs is not null && inputs.TryGetValue("Condition", out var conditionInput))
                activity.Condition = (InputArgument<bool>)conditionInput;
            return new(activity);
        }
    }

    private sealed record SequenceDescriptor;

    private sealed class SequenceConstructor : IActivityConstructor<SequenceDescriptor>
    {
        public static string SequenceDescriptorTypeKey => typeof(SequenceDescriptor).FullName!;
        public string DescriptorType => SequenceDescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new SequenceActivity());

        public ValueTask<IActivity> Construct(SequenceDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new SequenceActivity());
    }
}
