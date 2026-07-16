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
/// control-flow and explicit value-flow operations — <c>If</c> + <c>ForEach</c> + intrinsic <c>Set</c> — and runs
/// to <see cref="WorkflowExecutionStatus.Completed"/> through the real
/// <see cref="WorkflowExecutionHarness"/> (real in-process agent + scheduler, real JavaScript expression
/// evaluation via Jint — no counting mock, no hand-built resolution context).
///
/// The composed shape proves the activities genuinely interoperate, not merely run in isolation:
/// <list type="number">
/// <item><c>ForEach</c> iterates a three-item collection; on each pass its body is an intrinsic <c>Set</c> whose
/// portable expression reads the explicitly declared <c>count</c> parameter and commits the next frame revision.</item>
/// <item><c>If</c> branches on a portable expression over the explicitly declared <c>count</c> parameter, and its
/// Then branch is a final intrinsic <c>Set</c> that records the outcome
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
            { typeof(Break), typeof(ActivityUnit), [] },
            { typeof(Fault), typeof(ActivityUnit), ["message"] },
            { typeof(Inline), typeof(object), [nameof(Inline.Expression)] },
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
    public async Task IfForEachIntrinsicSet_ComposeAndRunToCompletion_ThroughTheRealHarness()
    {
        // Hand out a body-pass budget (5) comfortably beyond the collection size (3): a loop that failed to
        // observe its own per-pass mutation would not stop at the right count, and the If condition (== 3) would
        // not fire. Reaching the Then branch is therefore real proof the variable flowed across all three.
        await using var harness = NewHarness(
            "actexec-sequence",
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
        var rootFrame = Assert.IsType<VariableFrameState>(run.WorkflowState?.RootVariableFrame);
        Assert.Equal(3, rootFrame.Values["var-count"].InlineValue!.Value.GetInt32());
        Assert.Equal("all-iterated", rootFrame.Values["var-result"].InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task IfForEachIntrinsicSet_ComposeAndRunToCompletion_ThroughTheServerRequestHandlerPath()
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
        var rootFrame = Assert.IsType<VariableFrameState>(workflowState?.RootVariableFrame);
        Assert.Equal(3, rootFrame.Values["var-count"].InlineValue!.Value.GetInt32());
        Assert.Equal("all-iterated", rootFrame.Values["var-result"].InlineValue!.Value.GetString());
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

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<SequenceConstructor>()
            .WithConstructor<ForEachConstructor>()
            .WithConstructor<IfConstructor>()
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
    /// default 0) and <c>result</c> (String, default "none"), whose children are the ForEach loop and If branch.
    /// </summary>
    private static WorkflowExecutable NewComposedExecutable(IReadOnlyCollection<string> collection) =>
        WorkflowExecutionHarness.NewExecutable(NewComposedRoot(collection));

    private static ExecutableNode NewComposedRoot(IReadOnlyCollection<string> collection)
    {
        // (a) ForEach body: intrinsic Set count = args.current + 1. The expression receives only its declared
        // parameter and each pass commits the root frame before the next pass is scheduled.
        var incrementNode = SetVariableIntrinsicNode(
            nodeId: "node-incr",
            variableKey: "var-count",
            valueBinding: PortableVariableExpression("args.current + 1", "Int32", "current", "var-count"));

        var foreachNode = new ExecutableNode(
            executableNodeId: "node-foreach",
            authoredActivityId: "authored-foreach",
            activityType: typeof(ForEachActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ForEachConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new ForEachDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Collection"] = CollectionBinding(collection)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(ForEachActivity.BodySlotName, [incrementNode])],
            structure: new ExecutableActivityStructure(
                ForEachActivity.StructureKind,
                ForEachActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = "node-incr" })));

        // (b) If sees the same canonical root-frame value through an explicitly declared expression parameter.
        var setResultNode = SetVariableIntrinsicNode(
            nodeId: "node-set-result",
            variableKey: "var-result",
            valueBinding: CanonicalLiteral("value", "String", "all-iterated"));

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
                ["Condition"] = PortableVariableExpression("args.count === 3", "Boolean", "count", "var-count")
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

        var children = new[] { foreachNode, ifNode };
        var structurePayload = new
        {
            activities = children.Select(child => child.ExecutableNodeId).ToArray(),
            variables = new[]
            {
                RuntimeVariableDeclarationTestData.Create("var-count", "count", "Int32", 0),
                RuntimeVariableDeclarationTestData.Create("var-result", "result", "String", "none")
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

    private static ExecutableNode SetVariableIntrinsicNode(
        string nodeId,
        string variableKey,
        RuntimeInputBinding valueBinding) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "elsa.intrinsic.set",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Value] = valueBinding
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference(variableKey, VariableReference.WorkflowScopeId));

    private static RuntimeInputBinding CanonicalLiteral(string inputKey, string typeAlias, object? value)
    {
        var type = new ValueTypeDescriptor(typeAlias);
        var policy = ValueProtectionPolicy.InstanceInline;
        var envelope = value is null
            ? ValueEnvelope.Null(type, policy)
            : ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), policy);
        return new RuntimeInputBinding(
            inputKey,
            type,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: envelope);
    }

    private static RuntimeInputBinding CollectionBinding(IReadOnlyCollection<string> collection)
    {
        var type = new ValueTypeDescriptor("Elsa.Any", CollectionKind.List);
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(
            nameof(ForEachActivity.Collection),
            type,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(collection), policy),
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = ObjectTypeName
            });
    }

    private static RuntimeInputBinding PortableVariableExpression(
        string source,
        string resultTypeAlias,
        string parameterName,
        string variableKey)
    {
        var type = new ValueTypeDescriptor(resultTypeAlias);
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(
            parameterName == "count" ? "Condition" : WorkflowIntrinsicInputKeys.Value,
            type,
            policy,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "JavaScript",
                source,
                new RuntimeValueTypeDescriptor("alias", resultTypeAlias, null),
                parameters: new Dictionary<string, ExpressionParameterBinding>
                {
                    [parameterName] = new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, variableKey)
                },
                options: JsonSerializer.SerializeToElement(new { }),
                capabilityProfile: ExpressionCapabilityProfiles.BindingPureV1));
    }

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

    private sealed record ForEachDescriptor;

    private sealed class ForEachConstructor : IActivityConstructor<ForEachDescriptor>
    {
        public static string DescriptorTypeKey => typeof(ForEachDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new ForEachDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(ForEachDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new ForEachActivity());
    }

    private sealed record IfDescriptor;

    private sealed class IfConstructor : IActivityConstructor<IfDescriptor>
    {
        public static string DescriptorTypeKey => typeof(IfDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new IfDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(IfDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new IfActivity());
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
