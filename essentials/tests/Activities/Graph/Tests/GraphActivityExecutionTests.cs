using System.Text.Json;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Activation;
using Elsa.Activities.Graph.Runtime.Activities;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Graph.Runtime.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

public sealed class GraphActivityExecutionTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = CreateServiceProvider();

    [Fact]
    public async Task ActivationStrategy_CreatesATransientActivityFromThePinnedGraphDescriptor()
    {
        var descriptor = Descriptor();
        var runtimeDescriptor = RuntimeDescriptor(descriptor);
        var contract = RuntimeContract(runtimeDescriptor);
        var strategy = Assert.Single(
            _serviceProvider.GetServices<IActivityActivationStrategy>(),
            item => item is GraphActivityActivationStrategy);

        await using var lease = await strategy.ActivateAsync(
            new ActivityActivationStrategyRequest(contract, runtimeDescriptor));
        var graph = Assert.IsType<GraphActivity>(lease.Activity);

        Assert.Equal("definition-version-1", graph.Descriptor.DefinitionVersionId);
        Assert.Equal("sha256:template-1", graph.Descriptor.TemplateHash);
    }

    [Fact]
    public async Task Execute_SchedulesOnlyTheDirectGraphRootInTheOuterExecutionScope()
    {
        var descriptor = Descriptor();
        var graph = NewGraph(descriptor);
        var nestedLeaf = Node("nested-leaf");
        var entry = Node("entry", [new ExecutableChildSlot("Children", [nestedLeaf])]);
        var boundary = BoundaryNode(descriptor, [entry]);
        var context = Context(graph, boundary, State("outer-execution"));

        var start = await graph.ExecuteStructureAsync(context);

        Assert.True(start.IsDeferred);
        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("entry", request.ExecutableNodeId);
        Assert.Equal("outer-execution", request.SchedulingActivityExecutionId);
        Assert.Equal("outer-execution", request.SchedulingProvenance.ParentActivityExecutionId);
        Assert.Equal("outer-execution", request.SchedulingProvenance.ExecutionScopeId);
        Assert.Equal("graph-entry", request.SchedulingProvenance.SchedulingCause);

        await Assert.ThrowsAsync<InvalidOperationException>(() => graph.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "leaf-execution", "nested-leaf", [ActivityOutcomes.Done])).AsTask());

        var completion = await graph.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "entry-execution", "entry", [ActivityOutcomes.Done]));

        Assert.True(completion.IsComplete);
        Assert.Equal(ActivityOutcomes.Done, completion.OutcomeName);
        var preparation = await graph.PrepareCompletionCheckpointAsync(
            context,
            [],
            DateTimeOffset.UnixEpoch);

        Assert.Empty(preparation.DurableValueChanges);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition).Result));
    }

    [Fact]
    public async Task ChildCompletion_MapsAndCheckpointsTheSelectedBoundaryOutcome()
    {
        var descriptor = Descriptor(outcomeMappings:
        [
            new("Approved", "Accepted"),
            new("Rejected", "Declined")
        ]);
        var graph = NewGraph(descriptor);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("outer-execution"));

        var completion = await graph.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "entry-execution", "entry", ["Rejected"]));
        var preparation = await graph.PrepareCompletionCheckpointAsync(context, [], DateTimeOffset.UnixEpoch);

        Assert.Equal("Declined", completion.OutcomeName);
        Assert.Equal(
            "Declined",
            Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition).Outcome);
    }

    [Fact]
    public async Task ChildCompletion_MapsConvergingSourceOutcomesToTheSameBoundaryOutcome()
    {
        var descriptor = Descriptor(outcomeMappings:
        [
            new("Approved", "Accepted"),
            new("Rejected", "Accepted")
        ]);
        var graph = NewGraph(descriptor);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("outer-execution"));

        var completion = await graph.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "entry-execution", "entry", ["Rejected"]));
        var preparation = await graph.PrepareCompletionCheckpointAsync(context, [], DateTimeOffset.UnixEpoch);

        Assert.Equal("Accepted", completion.OutcomeName);
        Assert.Equal(
            "Accepted",
            Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition).Outcome);
    }

    [Theory]
    [InlineData()]
    [InlineData("Unknown")]
    [InlineData("Approved", "Unknown")]
    [InlineData("Approved", "Rejected")]
    public async Task ChildCompletion_RejectsAnOutcomeWithoutExactlyOneMapping(params string[] outcomes)
    {
        var descriptor = Descriptor(outcomeMappings:
        [
            new("Approved", "Accepted"),
            new("Rejected", "Declined")
        ]);
        var graph = NewGraph(descriptor);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("outer-execution"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => graph.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "entry-execution", "entry", outcomes)).AsTask());

        Assert.Contains("exactly one mapped outcome", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Descriptor_WithoutCompleteOutcomesDeserializesEmpty()
    {
        var json = JsonSerializer.SerializeToElement(Descriptor(), WebJson);
        var withoutField = JsonSerializer.Deserialize<JsonElement>(
            json.GetRawText().Replace(",\"completeOutcomes\":[]", "", StringComparison.Ordinal));

        var descriptor = withoutField.Deserialize<GraphActivityDescriptor>(WebJson)!;

        Assert.False(withoutField.TryGetProperty("completeOutcomes", out _));
        Assert.Empty(descriptor.CompleteOutcomes);
    }

    [Fact]
    public void Descriptor_RejectsDuplicateCompleteOutcomeReferenceKeys()
    {
        Assert.Throws<ArgumentException>(() => Descriptor(completeOutcomes: [new("fail", "Failed"), new("fail", "Other")]));
    }

    [Fact]
    public async Task CompleteNotification_SelectsTheOutcomeAndCancelsTheEntrySubtree()
    {
        var descriptor = Descriptor(completeOutcomes: [new("fail", "Failed"), new("succeed", "Succeeded")]);
        var graph = NewGraph(descriptor);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("outer-execution"),
            [new RuntimeLiveChildActivity("entry-execution", "entry", ActivityExecutionStatus.Running)]);

        var continuation = await graph.OnChildNotifiedAsync(context, CompleteNotification("fail"));
        var preparation = await graph.PrepareCompletionCheckpointAsync(context, [], DateTimeOffset.UnixEpoch);

        Assert.Equal("Failed", continuation.OutcomeName);
        Assert.Equal("Failed", Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition).Outcome);
        var cancellation = Assert.Single(context.GetChildSubtreeCancellationRequests());
        Assert.Equal("entry-execution", cancellation.ChildActivityExecutionId);
    }

    [Fact]
    public async Task CompleteNotification_SelectsTheOutcomeAlongsideOutcomeMappings()
    {
        var descriptor = Descriptor(
            outcomeMappings: [new("Approved", "Accepted")],
            completeOutcomes: [new("fail", "Failed")]);
        var graph = NewGraph(descriptor);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("outer-execution"));

        await graph.OnChildNotifiedAsync(context, CompleteNotification("fail"));
        var preparation = await graph.PrepareCompletionCheckpointAsync(context, [], DateTimeOffset.UnixEpoch);

        Assert.Equal("Failed", Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition).Outcome);
    }

    [Fact]
    public async Task CompleteNotification_FaultsOnAnUndeclaredReferenceKey()
    {
        var descriptor = Descriptor(completeOutcomes: [new("fail", "Failed")]);
        var graph = NewGraph(descriptor);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("outer-execution"));

        var continuation = await graph.OnChildNotifiedAsync(context, CompleteNotification("unknown"));

        Assert.Equal(GraphActivity.CompleteOutcomeUnknownFaultCode, continuation.Fault!.Code);
        Assert.Empty(context.GetChildSubtreeCancellationRequests());
    }

    [Fact]
    public async Task OtherNotificationCodes_LeaveTheBoundaryDeferred()
    {
        var descriptor = Descriptor(completeOutcomes: [new("fail", "Failed")]);
        var graph = NewGraph(descriptor);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("outer-execution"));

        var continuation = await graph.OnChildNotifiedAsync(
            context,
            new ActivityChildNotifiedContext("child-execution", "child", "something-else"));

        Assert.True(continuation.IsDeferred);
    }

    [Fact]
    public async Task NaturalCompletion_FaultsWhenOnlyCompleteNodesSelectOutcomes()
    {
        var descriptor = Descriptor(completeOutcomes: [new("fail", "Failed")]);
        var graph = NewGraph(descriptor);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("outer-execution"));

        var continuation = await graph.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "entry-execution", "entry", [ActivityOutcomes.Done]));

        Assert.Equal(GraphActivity.EntryCompletedWithoutMappingFaultCode, continuation.Fault!.Code);
    }

    [Fact]
    public async Task Execute_RejectsAnEntryThatIsOnlyANestedDescendant()
    {
        var descriptor = Descriptor();
        var graph = NewGraph(descriptor);
        var boundary = BoundaryNode(descriptor,
        [
            Node("different-root", [new ExecutableChildSlot("Children", [Node("entry")])])
        ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => graph.ExecuteStructureAsync(Context(graph, boundary, State("outer-execution"))).AsTask());

        Assert.Contains("direct child", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureInputs_DistinguishesExplicitNullAndMakesCapturedInputsReadOnly()
    {
        var scope = Scope();
        var descriptors = new[]
        {
            Input("explicit-null", Default("unused")),
            Input("defaulted", Default("fallback"))
        };
        var evaluations = 0;

        await scope.CaptureInputsAsync(
            descriptors,
            new Dictionary<string, object?> { ["explicit-null"] = null },
            new HashSet<string>(),
            (expression, _) =>
            {
                evaluations++;
                return ValueTask.FromResult<object?>(expression.Value.GetString());
            },
            DateTimeOffset.UnixEpoch);

        Assert.Equal(1, evaluations);
        Assert.Equal(JsonValueKind.Null, Assert.IsType<JsonElement>(await scope.GetRequiredInputAsync("explicit-null")).ValueKind);
        Assert.Equal("fallback", Assert.IsType<JsonElement>(await scope.GetRequiredInputAsync("defaulted")).GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.CaptureInputsAsync(
            descriptors,
            new Dictionary<string, object?>(),
            new HashSet<string>(),
            (_, _) => ValueTask.FromResult<object?>(null),
            DateTimeOffset.UnixEpoch).AsTask());
    }

    [Fact]
    public async Task CaptureInputs_UnknownDriverFailsBeforeEvaluationAndPublishesNothing()
    {
        var scope = Scope();
        var evaluations = 0;

        var exception = await Assert.ThrowsAsync<RuntimeDurableValueStorageDriverNotFoundException>(() => scope.CaptureInputsAsync(
            [Input("value", Default("fallback"), "unknown.driver")],
            new Dictionary<string, object?>(),
            new HashSet<string>(),
            (_, _) =>
            {
                evaluations++;
                return ValueTask.FromResult<object?>("value");
            },
            DateTimeOffset.UnixEpoch).AsTask());

        Assert.Contains("unknown.driver", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, evaluations);
        Assert.Empty(scope.Inputs);
    }

    [Fact]
    public async Task CaptureInputs_NonSerializableLateValuePublishesNoStagedInput()
    {
        var scope = Scope();
        var values = new Dictionary<string, object?>
        {
            ["serializable"] = "ready",
            ["opaque"] = new OpaqueValue(() => { })
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.CaptureInputsAsync(
            [Input("serializable"), Input("opaque")],
            values,
            new HashSet<string>(),
            (_, _) => ValueTask.FromResult<object?>(null),
            DateTimeOffset.UnixEpoch).AsTask());

        Assert.Empty(scope.Inputs);
    }

    [Fact]
    public async Task VariableAndOutputBatches_PublishNothingWhenLateEvaluationFails()
    {
        var variableScope = Scope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => variableScope.InitializeVariablesAsync(
            [Variable("first", Default("ok")), Variable("second", Default("fail"))],
            FailOnSentinel,
            DateTimeOffset.UnixEpoch).AsTask());
        Assert.Empty(variableScope.Variables);

        var outputScope = Scope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => outputScope.CaptureOutputsAsync(
            [Output("first"), Output("second")],
            [Mapping("first", Default("ok")), Mapping("second", Default("fail"))],
            new HashSet<string>(),
            FailOnSentinel,
            DateTimeOffset.UnixEpoch).AsTask());
        Assert.Empty(outputScope.Outputs);
    }

    [Fact]
    public async Task CaptureOutputs_EvaluatesBoundaryMappingOnceAndPublishesTheDurableResult()
    {
        var scope = Scope();
        var evaluations = 0;

        var captured = await scope.CaptureOutputsAsync(
            [Output("result")],
            [Mapping("result", Default("completed"))],
            new HashSet<string> { "result" },
            (expression, _) =>
            {
                evaluations++;
                return ValueTask.FromResult<object?>(expression.Value.GetString());
            },
            DateTimeOffset.UnixEpoch);

        var state = Assert.Single(captured);
        Assert.Equal(1, evaluations);
        Assert.Equal(GraphActivityScopeKeys.Output("outer-execution", "result"), state.DurableValueId);
        Assert.Equal("completed", state.InlineValue?.GetString());
        Assert.Same(state, scope.Outputs["result"]);
    }

    [Fact]
    public async Task SetOutput_checkpoints_only_the_nearest_invocations_value_and_rehydrates_it_for_the_caller()
    {
        var output = new GraphActivityOutputDescriptor("answer-key", Type("Int32"), WellKnownRuntimeDurableValueStorageDrivers.Json, "Answer");
        var descriptor = Descriptor(outputs: [output], requiredOutputReferenceKeys: [output.ReferenceKey]);
        var boundaryNode = BoundaryNode(descriptor, [Node("entry")]);
        var nearest = State("inner-call") with { Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ActivityExecutionScope] = RuntimeMetadataKeys.ActivityExecutionScopeClosed }, ParentActivityExecutionId = "outer-call" };
        var enclosing = nearest with { Execution = nearest.Execution with { ActivityExecutionId = "outer-call" }, ParentActivityExecutionId = null };
        var sibling = nearest with { Execution = nearest.Execution with { ActivityExecutionId = "second-call" } };
        var activityStore = _serviceProvider.GetRequiredService<IActivityExecutionStateStore>();
        foreach (var owner in new[] { nearest, enclosing, sibling })
            await activityStore.SaveAsync(owner);
        var identity = new WorkflowExecutableIdentity("artifact", "workflow", "workflow-version", "1", "sha256:workflow");
        var now = DateTimeOffset.UnixEpoch;
        await _serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new WorkflowExecutionState(
            "workflow-execution", identity, WorkflowExecutionStatus.Running, null, now, now, now, null, null, null, null, new Dictionary<string, string>()));
        var store = _serviceProvider.GetRequiredService<IDurableValueStateStore>();

        async Task Assign(string invocation, ActivityExecutionState? owner, int number)
        {
            var binding = new Dictionary<string, RuntimeInputBinding>
            {
                ["name"] = LiteralBinding("name", "String", "Answer"),
                ["value"] = LiteralBinding("value", "Int32", number)
            };
            var target = owner is null ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.SerializeToElement(new
            {
                referenceKey = output.ReferenceKey, type = output.Type, storageDriverKey = output.StorageDriverKey, isNullable = false
            });
            var node = new ExecutableNode("set", "authored-set", "elsa.intrinsic.setoutput", "1.0.0",
                WellKnownRuntimeActivityConsumers.Intrinsic, target, binding, new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.SetOutput,
                descriptorSchemaVersion: owner is null ? RuntimeActivityDescriptor.InitialSchemaVersion : WorkflowIntrinsicDescriptorSchemas.ActivityOutput);
            var scheduled = State(invocation) with
            {
                Execution = new ActivityExecution(invocation, "workflow-execution", "set", "authored-set", node.ActivityType, "1.0.0"),
                ParentActivityExecutionId = owner?.InvocationId,
                Status = ActivityExecutionStatus.Scheduled
            };
            await activityStore.SaveAsync(scheduled);
            var executable = new WorkflowExecutable(identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), now,
                new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
            var payload = new RuntimeStartActivityCommandPayload(identity, "set", invocation, RuntimeStartActivityCommandPayload.ScheduledActivityReason);
            var work = new RuntimeSchedulerWorkItem(invocation, "workflow-execution", invocation, WorkflowExecutionCommandKind.StartActivity,
                invocation, invocation, now, now, 1, JsonSerializer.SerializeToElement(payload), new Dictionary<string, string>(), new Dictionary<string, string>());
            var commit = await _serviceProvider.GetRequiredService<WorkflowIntrinsicExecutor>().ExecuteAsync(work, payload, executable, node, scheduled);
            var write = Assert.Single(commit.StateChanges.DurableValues);
            Assert.Equal(ActivityExecutionStatus.Completed, Assert.Single(commit.StateChanges.ActivityExecutions).State.Status);
            await store.SaveAsync(write.State);
        }

        await Assign("write-root", null, 3);
        await Assign("write-outer", enclosing, 7);
        await Assign("write-inner", nearest, 13);
        await Assign("overwrite-inner", nearest, 23);
        var persisted = await store.ListAllDurableValueStatesAsync("workflow-execution");
        Assert.Equal(3, persisted.Count);
        Assert.Equal(3, Assert.Single(persisted, x => x.ValueId == RuntimeWorkflowStateSeed.OutputValueIdPrefix + "Answer").InlineValue!.Value.GetInt32());

        async Task<int> Complete(ActivityExecutionState owner)
        {
            // A fresh activation sees only the checkpointed rows for its own invocation.
            var graph = NewGraph(descriptor);
            var preparation = await graph.PrepareCompletionCheckpointAsync(Context(graph, boundaryNode, owner), persisted, now);
            Assert.Empty(preparation.DurableValueChanges);
            var result = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
                Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition).Result);
            return Assert.IsType<JsonElement>(result[output.ReferenceKey]).GetInt32();
        }

        Assert.Equal(23, await Complete(nearest));
        Assert.Equal(7, await Complete(enclosing));
        var unassigned = await Assert.ThrowsAsync<InvalidOperationException>(() => Complete(sibling));
        Assert.Contains("Required graph output", unassigned.Message);
    }

    private static RuntimeInputBinding LiteralBinding<T>(string key, string alias, T value)
    {
        var type = new ValueTypeDescriptor(alias);
        return new RuntimeInputBinding(key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    [Fact]
    public async Task PrepareCompletion_ReturnsTypedObjectResultForContractProjection()
    {
        var descriptor = Descriptor(
            outputs: [Output("result")],
            outputMappings: [Mapping("result", Default("completed"))],
            requiredOutputReferenceKeys: ["result"]);
        var graph = NewGraph(descriptor);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("outer-execution"));

        var preparation = await graph.PrepareCompletionCheckpointAsync(
            context,
            [],
            DateTimeOffset.UnixEpoch);

        Assert.Single(preparation.DurableValueChanges);
        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition);
        Assert.Equal(ActivityOutcomes.Done, completion.Outcome);
        var result = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(completion.Result);
        Assert.Equal("completed", Assert.IsType<JsonElement>(result["result"]).GetString());
    }

    [Fact]
    public async Task PrepareCompletion_EvaluatesPortableJavaScriptAgainstCapturedPublicInputs()
    {
        var descriptor = Descriptor(
            inputs:
            [
                Input("a") with { Name = "A" },
                Input("b") with { Name = "B" }
            ],
            outputs: [new GraphActivityOutputDescriptor("sum", Type("Int32"), WellKnownRuntimeDurableValueStorageDrivers.Json)],
            outputMappings:
            [
                Mapping("sum", new GraphActivityExpression("JavaScript", JsonSerializer.SerializeToElement("args.A + args.B")))
            ],
            requiredOutputReferenceKeys: ["sum"],
            outcomeMappings: [new("Calculated", "Calculated")]);
        var preparation = await PrepareGraphCompletionAsync(
            descriptor,
            new Dictionary<string, object?> { ["A"] = 19, ["B"] = 23 },
            ["Calculated"]);

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition);
        var result = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(completion.Result);
        Assert.Equal(42, Assert.IsType<JsonElement>(result["sum"]).GetInt32());
        Assert.Equal("Calculated", completion.Outcome);
    }

    [Fact]
    public async Task PrepareCompletion_ResolvesNativeInputMappingsFromCapturedPublicInputs()
    {
        var descriptor = Descriptor(
            inputs: [Input("a", name: "A")],
            outputs: [new GraphActivityOutputDescriptor("copy", Type("Int32"), WellKnownRuntimeDurableValueStorageDrivers.Json)],
            outputMappings:
            [
                Mapping("copy", new GraphActivityExpression("Input", JsonSerializer.SerializeToElement(new { referenceKey = "a" })))
            ],
            requiredOutputReferenceKeys: ["copy"]);
        var preparation = await PrepareGraphCompletionAsync(
            descriptor,
            new Dictionary<string, object?> { ["A"] = 42 });

        var result = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition).Result);
        Assert.Equal(42, Assert.IsType<JsonElement>(result["copy"]).GetInt32());
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("activity-graph")]
    [InlineData(null)]
    public async Task VariableOutputMapping_ReturnsOnlyTheCurrentInvocationFinalValue(string? scopeId)
    {
        var variable = new GraphActivityVariableDescriptor(
            "running-total", "RenamedTotal", Type("Int32"), WellKnownRuntimeDurableValueStorageDrivers.Json,
            new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement(9)));
        var descriptor = Descriptor(
            variables: [variable],
            outputs: [Output("result") with { Type = Type("Int32") }],
            outputMappings: [Mapping("result", VariableRead(variable.ReferenceKey, scopeId))],
            requiredOutputReferenceKeys: ["result"]);
        var factory = new GraphActivityScopeFactory(Registry());
        var now = DateTimeOffset.UnixEpoch;
        var persisted = new List<DurableValueState>();
        foreach (var (executionId, value) in new[] { ("outer-call", 17), ("inner-call", 42) })
        {
            var graph = NewGraph(descriptor);
            var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State(executionId));
            var entry = await graph.PrepareEntryCheckpointAsync(context, new Dictionary<string, object?>(), now);
            var scope = factory.Rehydrate(descriptor, context.WorkflowExecutionId, executionId, entry.Select(change => change.State).ToArray());
            persisted.Add(await scope.SetVariableAsync(variable, value, now.AddMinutes(1)));
        }

        foreach (var (executionId, expected) in new[] { ("inner-call", 42), ("outer-call", 17) })
        {
            var resumed = NewGraph(descriptor);
            var context = Context(resumed, BoundaryNode(descriptor, [Node("entry")]), State(executionId));
            var prepared = await resumed.PrepareCompletionCheckpointAsync(context, persisted, now.AddMinutes(2));
            var result = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
                Assert.IsAssignableFrom<IActivityCompletionTransition>(prepared.Transition).Result);
            var output = Assert.Single(result);
            Assert.Equal("result", output.Key);
            Assert.Equal(expected, Assert.IsType<JsonElement>(output.Value).GetInt32());
        }
    }

    [Theory]
    [InlineData("running-total", "foreign", "not visible")]
    [InlineData("DisplayName", "workflow", "undeclared")]
    [InlineData("running-total", "workflow", "returned null")]
    public async Task VariableOutputMapping_RejectsForeignScopesDisplayNamesAndNullForNumericOutput(string key, string scopeId, string expectedError)
    {
        var descriptor = Descriptor(
            variables: [Variable("running-total", null) with { Name = "DisplayName" }],
            outputs: [Output("result") with { Type = Type("Int32") }],
            outputMappings: [Mapping("result", VariableRead(key, scopeId))]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PrepareGraphCompletionAsync(descriptor, new Dictionary<string, object?>()));
        Assert.Contains(expectedError, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VariableOutputMapping_RequiresTheReferencedCheckpointRow()
    {
        var descriptor = Descriptor(
            variables: [Variable("key", Default("initial"))],
            outputs: [Output("result")],
            outputMappings: [Mapping("result", VariableRead("key", "workflow"))]);
        var graph = NewGraph(descriptor);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("resumed-call"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            graph.PrepareCompletionCheckpointAsync(context, [], DateTimeOffset.UnixEpoch).AsTask());
        Assert.Contains("no captured value", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Array", true)]
    [InlineData("Single", false)]
    public async Task VariableOutputMapping_UsesTheDeclaredOutputCollectionShape(string collectionKind, bool acceptsArray)
    {
        var array = JsonSerializer.SerializeToElement(new[] { 2, 4 });
        var arrayType = JsonSerializer.SerializeToElement(new { alias = "Int32", collectionKind = "Array" });
        var descriptor = Descriptor(
            variables: [Variable("numbers", new GraphActivityExpression("Literal", array)) with { Type = arrayType }],
            outputs: [Output("result") with { Type = JsonSerializer.SerializeToElement(new { alias = "Int32", collectionKind }) }],
            outputMappings: [Mapping("result", VariableRead("numbers", "workflow"))]);

        var completion = PrepareGraphCompletionAsync(descriptor, new Dictionary<string, object?>());
        if (!acceptsArray)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => completion);
            Assert.Contains("does not match declared type", error.Message, StringComparison.Ordinal);
            return;
        }

        var result = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            Assert.IsAssignableFrom<IActivityCompletionTransition>((await completion).Transition).Result);
        Assert.True(JsonElement.DeepEquals(array, Assert.IsType<JsonElement>(result["result"])));
    }

    private static GraphActivityExpression VariableRead(string referenceKey, string? declaringScopeId) =>
        new("Variable", JsonSerializer.SerializeToElement(new { referenceKey, declaringScopeId }));

    [Fact]
    public async Task PrepareCompletion_DoesNotRequireAnUnreferencedOptionalInput()
    {
        var descriptor = Descriptor(
            inputs: [Input("optional", name: "Optional")],
            outputs: [new GraphActivityOutputDescriptor("result", Type("Int32"), WellKnownRuntimeDurableValueStorageDrivers.Json)],
            outputMappings: [Mapping("result", new GraphActivityExpression("JavaScript", JsonSerializer.SerializeToElement("40 + 2")))],
            requiredOutputReferenceKeys: ["result"]);
        var preparation = await PrepareGraphCompletionAsync(descriptor, new Dictionary<string, object?>());

        var result = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition).Result);
        Assert.Equal(42, Assert.IsType<JsonElement>(result["result"]).GetInt32());
    }

    [Fact]
    public async Task PrepareCompletion_ExposesCapturedGraphVariablesToPortableJavaScript()
    {
        var descriptor = Descriptor(
            variables: [new GraphActivityVariableDescriptor(
                "running-total",
                "RunningTotal",
                Type("Int32"),
                WellKnownRuntimeDurableValueStorageDrivers.Json,
                new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement(40)))],
            outputs: [new GraphActivityOutputDescriptor("result", Type("Int32"), WellKnownRuntimeDurableValueStorageDrivers.Json)],
            outputMappings: [Mapping("result", new GraphActivityExpression("JavaScript", JsonSerializer.SerializeToElement("variables.RunningTotal + 2")))],
            requiredOutputReferenceKeys: ["result"]);
        var preparation = await PrepareGraphCompletionAsync(descriptor, new Dictionary<string, object?>());

        var result = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            Assert.IsAssignableFrom<IActivityCompletionTransition>(preparation.Transition).Result);
        Assert.Equal(42, Assert.IsType<JsonElement>(result["result"]).GetInt32());
    }

    [Fact]
    public async Task PrepareCompletion_RejectsDuplicatePublicNames()
    {
        var duplicateNameDescriptor = Descriptor(
            inputs: [Input("a", name: "A"), Input("b", name: "A")],
            outputs: [new GraphActivityOutputDescriptor("result", Type("Int32"), WellKnownRuntimeDurableValueStorageDrivers.Json)],
            outputMappings: [Mapping("result", new GraphActivityExpression("JavaScript", JsonSerializer.SerializeToElement("args.A")))],
            requiredOutputReferenceKeys: ["result"]);
        var duplicateException = await Assert.ThrowsAsync<InvalidOperationException>(() => PrepareGraphCompletionAsync(
            duplicateNameDescriptor,
            new Dictionary<string, object?> { ["A"] = 1 }));
        Assert.Contains("declared more than once", duplicateException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareCompletion_RejectsMismatchedResultTypes()
    {
        var mismatchDescriptor = Descriptor(
            outputs: [new GraphActivityOutputDescriptor("result", Type("Int32"), WellKnownRuntimeDurableValueStorageDrivers.Json)],
            outputMappings: [Mapping("result", new GraphActivityExpression("JavaScript", JsonSerializer.SerializeToElement("'not-an-int'")))],
            requiredOutputReferenceKeys: ["result"]);
        var typeException = await Assert.ThrowsAsync<InvalidOperationException>(() => PrepareGraphCompletionAsync(
            mismatchDescriptor,
            new Dictionary<string, object?>()));
        Assert.Contains("does not match declared type 'Int32'", typeException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalDriver_RehydratesAndReadsANonSerializableInputAfterSuspension()
    {
        var externalDriver = new TestExternalStorageDriver();
        var registry = Registry(externalDriver);
        var factory = new GraphActivityScopeFactory(registry);
        var descriptor = Descriptor(inputs: [Input("opaque", storageDriverKey: externalDriver.DriverKey)]);
        var original = factory.Create(descriptor, "workflow-execution", "outer-execution");
        var opaque = new OpaqueValue(() => { });

        var persisted = await original.CaptureInputsAsync(
            descriptor.Inputs,
            new Dictionary<string, object?> { ["opaque"] = opaque },
            new HashSet<string>(),
            (_, _) => ValueTask.FromResult<object?>(null),
            DateTimeOffset.UnixEpoch);
        Assert.Equal(DurableValueStorage.External, Assert.Single(persisted).Storage);

        var rehydrated = factory.Rehydrate(
            descriptor,
            "workflow-execution",
            "outer-execution",
            persisted);

        Assert.Same(opaque, await rehydrated.GetRequiredInputAsync("opaque"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rehydrated.CaptureInputsAsync(
            descriptor.Inputs,
            new Dictionary<string, object?>(),
            new HashSet<string>(),
            (_, _) => ValueTask.FromResult<object?>(null),
            DateTimeOffset.UnixEpoch).AsTask());
    }

    [Fact]
    public async Task Rehydrate_RejectsPersistedStateWhoseCodecDoesNotMatchTheBoundaryDeclaration()
    {
        var externalDriver = new TestExternalStorageDriver();
        var factory = new GraphActivityScopeFactory(Registry(externalDriver));
        var descriptor = Descriptor(inputs: [Input("opaque", storageDriverKey: externalDriver.DriverKey)]);
        var original = factory.Create(descriptor, "workflow-execution", "outer-execution");
        var captured = Assert.Single(await original.CaptureInputsAsync(
            descriptor.Inputs,
            new Dictionary<string, object?> { ["opaque"] = new OpaqueValue(() => { }) },
            new HashSet<string>(),
            (_, _) => ValueTask.FromResult<object?>(null),
            DateTimeOffset.UnixEpoch));
        var tampered = new DurableValueState(
            captured.DurableValueId,
            captured.WorkflowExecutionId,
            captured.ValueId,
            new RuntimeValueTypeDescriptor(captured.Type.Kind, WellKnownRuntimeDurableValueStorageDrivers.Json, captured.Type.Schema),
            captured.Lifecycle,
            captured.Storage,
            captured.InlineValue,
            captured.ExternalReference,
            captured.SourceActivityExecutionId,
            captured.CapturedAt,
            captured.Metadata);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Rehydrate(
            descriptor,
            "workflow-execution",
            "outer-execution",
            [tampered]));

        Assert.Contains("declared type and storage driver", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopeFactory_FindsNearestScopeAndUsesCollisionSafeRoleKeys()
    {
        var factory = new GraphActivityScopeFactory(Registry());
        var descriptor = Descriptor();
        var outer = factory.Create(descriptor, "workflow-execution", "outer-execution");
        var nested = factory.Create(descriptor, "workflow-execution", "nested-execution");
        var descendant = State("descendant", "nested-execution");

        Assert.Same(nested, factory.FindNearest(descendant, [outer, nested]));
        Assert.NotEqual(
            GraphActivityScopeKeys.Input("ab", "c"),
            GraphActivityScopeKeys.Input("a", "bc"));
        Assert.NotEqual(
            GraphActivityScopeKeys.Input("outer-execution", "same"),
            GraphActivityScopeKeys.Variable("outer-execution", "same"));
    }

    [Fact]
    public void RuntimeFeature_RegistersGraphRuntimeServices()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new GraphActivitiesRuntimeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var capability = Assert.Single(
            provider.GetServices<IRuntimeActivityConsumerCapability>(),
            item => item.ConsumerKey == WellKnownRuntimeActivityConsumers.GraphActivity);
        Assert.Equal([RuntimeActivityDescriptor.InitialSchemaVersion], capability.SupportedSchemaVersions);
        Assert.NotNull(provider.GetRequiredService<GraphActivityScopeFactory>());
        Assert.NotNull(provider.GetRequiredService<GraphActivityRecovery>());
        Assert.IsType<JsonRuntimeDurableValueStorageDriver>(
            provider.GetRequiredService<IRuntimeDurableValueStorageDriverRegistry>()
                .GetRequired(WellKnownRuntimeDurableValueStorageDrivers.Json));
    }

    public void Dispose() => _serviceProvider.Dispose();

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new GraphActivitiesRuntimeFeature().ConfigureServices(services);
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        new SerializationFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(int), "Int32");
        return provider;
    }

    private GraphActivityScope Scope() =>
        new GraphActivityScopeFactory(Registry()).Create(Descriptor(), "workflow-execution", "outer-execution");

    private static IRuntimeDurableValueStorageDriverRegistry Registry(
        params IRuntimeDurableValueStorageDriver[] additionalDrivers) =>
        new RuntimeDurableValueStorageDriverRegistry(
            new IRuntimeDurableValueStorageDriver[] { new JsonRuntimeDurableValueStorageDriver() }.Concat(additionalDrivers));

    private SimpleActivityExecutionContext Context(
        GraphActivity graph,
        ExecutableNode node,
        ActivityExecutionState state,
        IReadOnlyCollection<RuntimeLiveChildActivity>? liveChildActivities = null) =>
        new(
            graph,
            CancellationToken.None,
            "workflow-execution",
            new WorkflowExecutableIdentity("artifact", "workflow", "workflow-version", "1", "sha256:workflow"),
            schedulerWorkItem: null,
            executableNode: node,
            activityExecutionState: state,
            liveChildActivities: liveChildActivities);

    private static ActivityChildNotifiedContext CompleteNotification(string referenceKey) =>
        new(
            "complete-execution",
            "complete",
            "elsa.graph.complete",
            payload: JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["outcome"] = referenceKey }));

    private GraphActivity NewGraph(GraphActivityDescriptor descriptor) =>
        new(
            descriptor,
            _serviceProvider.GetRequiredService<GraphActivityScopeFactory>(),
            _serviceProvider.GetRequiredService<GraphActivityRecovery>(),
            _serviceProvider.GetRequiredService<IDurableValueStateStore>(),
            _serviceProvider.GetService<Elsa.Expressions.Core.Contracts.IPortableExpressionEvaluator>(),
            _serviceProvider.GetService<IWellKnownTypeRegistry>());

    private async Task<RuntimeActivityCompletionCheckpointPreparation> PrepareGraphCompletionAsync(
        GraphActivityDescriptor descriptor,
        IReadOnlyDictionary<string, object?> effectiveInputs,
        IReadOnlyCollection<string>? childOutcomeNames = null)
    {
        await using var lease = await ActivateGraphAsync(descriptor);
        var graph = Assert.IsType<GraphActivity>(lease.Activity);
        var context = Context(graph, BoundaryNode(descriptor, [Node("entry")]), State("outer-execution"));
        var inputChanges = await graph.PrepareEntryCheckpointAsync(context, effectiveInputs, DateTimeOffset.UnixEpoch);
        await graph.OnChildCompletedAsync(new ActivityChildCompletedContext(
            context,
            "entry-execution",
            "entry",
            childOutcomeNames ?? [ActivityOutcomes.Done]));

        return await graph.PrepareCompletionCheckpointAsync(
            context,
            inputChanges.Select(change => change.State).ToArray(),
            DateTimeOffset.UnixEpoch);
    }

    private async ValueTask<ActivityActivationLease> ActivateGraphAsync(GraphActivityDescriptor descriptor)
    {
        var runtimeDescriptor = RuntimeDescriptor(descriptor);
        var strategy = Assert.Single(
            _serviceProvider.GetServices<IActivityActivationStrategy>(),
            item => item is GraphActivityActivationStrategy);
        return await strategy.ActivateAsync(new ActivityActivationStrategyRequest(RuntimeContract(runtimeDescriptor), runtimeDescriptor));
    }

    private static ExecutableNode BoundaryNode(
        GraphActivityDescriptor descriptor,
        IReadOnlyCollection<ExecutableNode> children) =>
        new(
            "boundary",
            "authored-boundary",
            "graph",
            "1.0.0",
            RuntimeDescriptor(descriptor),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string> { ["graph.templateHash"] = descriptor.TemplateHash },
            [new ExecutableChildSlot("Graph.Entry", children)]);

    private static ExecutableNode Node(
        string nodeId,
        IReadOnlyCollection<ExecutableChildSlot>? slots = null) =>
        new(
            nodeId,
            $"authored-{nodeId}",
            "test/probe",
            "1.0.0",
            new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            slots);

    private static RuntimeActivityDescriptor RuntimeDescriptor(GraphActivityDescriptor descriptor) =>
        new(
            WellKnownRuntimeActivityConsumers.GraphActivity,
            RuntimeActivityDescriptor.InitialSchemaVersion,
            JsonSerializer.SerializeToElement(descriptor, WebJson));

    private static Elsa.Activities.Runtime.Core.Models.ActivityContract RuntimeContract(
        RuntimeActivityDescriptor descriptor) =>
        new(
            "graph",
            "1.0.0",
            WellKnownRuntimeActivityConsumers.GraphActivity,
            descriptor.Payload,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Object"),
                true,
                ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
                []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(
                WellKnownRuntimeActivityConsumers.GraphActivity,
                WellKnownRuntimeActivityConsumers.GraphActivity));

    private static ActivityExecutionState State(string activityExecutionId, string? executionScopeId = null) =>
        new(
            new ActivityExecution(activityExecutionId, "workflow-execution", "boundary", "authored-boundary", "graph", "1.0.0"),
            ActivityExecutionStatus.Running,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.From(
                "workflow-execution",
                null,
                null,
                null,
                null,
                null,
                executionScopeId,
                "test"),
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static GraphActivityDescriptor Descriptor(
        IReadOnlyList<GraphActivityInputDescriptor>? inputs = null,
        IReadOnlyList<GraphActivityVariableDescriptor>? variables = null,
        IReadOnlyList<GraphActivityOutputDescriptor>? outputs = null,
        IReadOnlyList<GraphActivityOutputMappingDescriptor>? outputMappings = null,
        IReadOnlyCollection<string>? requiredOutputReferenceKeys = null,
        IReadOnlyList<GraphActivityOutcomeMappingDescriptor>? outcomeMappings = null,
        IReadOnlyList<GraphActivityCompleteOutcomeDescriptor>? completeOutcomes = null) =>
        new(
            "definition-1",
            "definition-version-1",
            "1.0.0",
            "sha256:template-1",
            "entry",
            entryOccurrenceId: null,
            inputs ?? [],
            variables: variables ?? [],
            outputs: outputs ?? [],
            outputMappings: outputMappings ?? [],
            requiredInputReferenceKeys: [],
            requiredOutputReferenceKeys: requiredOutputReferenceKeys?.ToArray() ?? [],
            outcomeMappings: outcomeMappings,
            completeOutcomes: completeOutcomes);

    private static GraphActivityInputDescriptor Input(
        string referenceKey,
        GraphActivityExpression? @default = null,
        string storageDriverKey = WellKnownRuntimeDurableValueStorageDrivers.Json,
        string? name = null) =>
        new(referenceKey, Type(), storageDriverKey, @default, name);

    private static GraphActivityVariableDescriptor Variable(string referenceKey, GraphActivityExpression? initialValue) =>
        new(referenceKey, referenceKey, Type(), WellKnownRuntimeDurableValueStorageDrivers.Json, initialValue);

    private static GraphActivityOutputDescriptor Output(string referenceKey) =>
        new(referenceKey, Type(), WellKnownRuntimeDurableValueStorageDrivers.Json);

    private static GraphActivityOutputMappingDescriptor Mapping(string referenceKey, GraphActivityExpression source) =>
        new(referenceKey, source);

    private static GraphActivityExpression Default(string value) =>
        new("Literal", JsonSerializer.SerializeToElement(value));

    private static JsonElement Type(string alias = "object") => JsonSerializer.SerializeToElement(new { alias });

    private static ValueTask<object?> FailOnSentinel(
        GraphActivityExpression expression,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return expression.Value.GetString() == "fail"
            ? ValueTask.FromException<object?>(new InvalidOperationException("late failure"))
            : ValueTask.FromResult<object?>(expression.Value.GetString());
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record OpaqueValue(Action Callback);

    private sealed class TestExternalStorageDriver : IRuntimeDurableValueStorageDriver
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public string DriverKey => "test.external";

        public ValueTask<RuntimeDurableValueEncoding> EncodeAsync(
            object? value,
            RuntimeValueTypeDescriptor type,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = $"value-{_values.Count + 1}";
            _values.Add(locator, value);
            return ValueTask.FromResult(new RuntimeDurableValueEncoding(
                DurableValueStorage.External,
                null,
                new DurableValueExternalReference("test", locator, new Dictionary<string, string>())));
        }

        public ValueTask<object?> DecodeAsync(
            DurableValueState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!StringComparer.Ordinal.Equals(state.Type.Id, DriverKey) ||
                state.Storage != DurableValueStorage.External ||
                state.ExternalReference is not { } reference)
            {
                throw new InvalidOperationException("Invalid external durable value state.");
            }

            return ValueTask.FromResult(_values[reference.Locator]);
        }
    }
}
