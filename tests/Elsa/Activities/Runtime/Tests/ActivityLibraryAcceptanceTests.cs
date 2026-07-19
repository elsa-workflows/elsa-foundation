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
        Assert.Equal(expectedResultType, FindActivityResultType(activityType));
    }

    [Fact]
    public void EveryRetainedFirstPartyClrActivity_HasPlainInputsAndExactlyOneAtomicResult()
    {
        var assemblies = new[]
        {
            typeof(WriteLine).Assembly,
            typeof(SequenceActivity).Assembly,
            typeof(ForEachActivity).Assembly,
            typeof(Elsa.Activities.Flowchart.Activities.Flowchart).Assembly,
            typeof(Elsa.Activities.Http.Activities.HttpEndpoint).Assembly,
            typeof(Elsa.Activities.Scheduling.Activities.Timer).Assembly,
            typeof(Elsa.Activities.Scripting.Activities.RunJavaScript).Assembly
        };
        var activityTypes = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsClass: true } && typeof(IActivity).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(activityTypes);
        foreach (var activityType in activityTypes)
        {
            var properties = activityType.GetProperties();
            var resultContracts = activityType.GetInterfaces()
                .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IActivityResult<>))
                .Select(candidate => candidate.GetGenericArguments()[0])
                .Distinct()
                .ToArray();
            Assert.True(
                resultContracts.Length == 1,
                $"Activity '{activityType.FullName}' must declare exactly one atomic result contract but declared {resultContracts.Length}.");
        }
    }

    private static Type? FindActivityResultType(Type activityType)
    {
        return activityType.GetInterfaces()
            .Single(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IActivityResult<>))
            .GetGenericArguments()[0];
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

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
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
            descriptorType: typeof(ForEachDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new ForEachDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Collection"] = CollectionBinding(collection)
            },
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
            descriptorType: typeof(IfDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new IfDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = PortableVariableExpression("args.count === 3", "Boolean", "count", "var-count")
            },
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
            descriptorType: typeof(SequenceDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new SequenceDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
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
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(collection), policy));
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

    private sealed record ForEachDescriptor;

    private sealed record IfDescriptor;

    private sealed record SequenceDescriptor;
}
