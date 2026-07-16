using System.Text.Json;
using Elsa.Activities.ForEach.Exceptions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;

namespace Elsa.Activities.ForEach.Tests;

public sealed class ForEachActivityTests : IDisposable
{
    private const string ForEachNodeId = "node-foreach";
    private const string BodyNodeId = "node-body";
    private const string ForEachExecutionId = "actexec-foreach";

    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    public void Dispose() => _serviceProvider.Dispose();

    [Fact]
    public async Task Execute_SchedulesBodyForFirstItem_WithIterationProvenanceAndItem()
    {
        var context = NewContext(new[] { "x", "y", "z" });

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal(BodyNodeId, request.ExecutableNodeId);
        Assert.Equal(ForEachExecutionId, request.SchedulingActivityExecutionId);
        // ADR 0028 step 3: the body is scheduled under a distinct per-pass IterationId.
        Assert.Equal($"{ForEachExecutionId}:foreach-iteration:0", request.SchedulingProvenance.IterationId);
        Assert.Equal("0", request.Metadata["foreach.iterationIndex"]);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task Execute_PreservesCollectionProtectionPolicyOnIterationItem()
    {
        var policy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "mask");
        var context = NewContext(new[] { "secret" }, CollectionBinding(policy));

        await ExecuteAsync(context);

        var item = Assert.Single(context.GetChildActivityScheduleRequests()).IterationFrame!.Values[ForEachActivity.CurrentItemVariableName];
        Assert.Equal("String", item.Type.Alias);
        Assert.Equal(CollectionKind.Single, item.Type.CollectionKind);
        Assert.Equal(policy, item.Policy);
    }

    [Fact]
    public async Task Execute_RejectsTransientCollectionBeforeSchedulingDurableIterationFrame()
    {
        var context = NewContext(new[] { "ephemeral" }, CollectionBinding(ValueProtectionPolicy.Transient));

        var exception = await Assert.ThrowsAsync<ForEachExecutionException>(() => ExecuteAsync(context).AsTask());

        Assert.Contains("transient Collection", exception.Message, StringComparison.Ordinal);
        Assert.Empty(context.GetChildActivityScheduleRequests());
    }

    [Fact]
    public async Task Execute_CompletesWithDone_WhenCollectionIsEmpty()
    {
        var context = NewContext(Array.Empty<string>());

        await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task Execute_CompletesWithDone_WhenCollectionIsNull()
    {
        var context = NewContext(collection: null);

        await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task Execute_Throws_WhenCollectionIsNotEnumerable()
    {
        var context = NewContext(collection: 42);

        await Assert.ThrowsAsync<ForEachExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task OnChildCompleted_SchedulesNextItem_WithIncrementedIterationId()
    {
        var context = NewContext(new[] { "x", "y", "z" });

        await ((ForEachActivity)context.Activity).OnChildCompletedAsync(NewCompletion(context, iterationIndex: 0));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal(BodyNodeId, request.ExecutableNodeId);
        Assert.Equal($"{ForEachExecutionId}:foreach-iteration:1", request.SchedulingProvenance.IterationId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithDone_AfterLastItem()
    {
        var context = NewContext(new[] { "x", "y", "z" });

        // Item index 2 is the last of three; completing it exhausts the collection.
        await ((ForEachActivity)context.Activity).OnChildCompletedAsync(NewCompletion(context, iterationIndex: 2));

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    // The completion callback must publish the next item in the typed iteration frame. This pins the
    // identity of the item resolved on advance (off-by-one guard): completing index 0 schedules "y".
    [Fact]
    public async Task OnChildCompleted_PublishesNextItemSerializedValue()
    {
        var context = NewContext(new[] { "x", "y", "z" });

        await ((ForEachActivity)context.Activity).OnChildCompletedAsync(NewCompletion(context, iterationIndex: 0));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        var item = Assert.IsType<ValueEnvelope>(request.IterationFrame!.Values[ForEachActivity.CurrentItemVariableName]);
        Assert.Equal("y", item.InlineValue!.Value.GetString());
        Assert.DoesNotContain(RuntimeMetadataKeys.LoopIterationItemValue, request.Metadata);
    }

    // The item value on the completion path must survive JsonElement unwrapping identically to Execute:
    // a JSON array collection input resolves the scalar CLR item stored in the typed envelope.
    [Fact]
    public async Task OnChildCompleted_ResolvesJsonElementItem_ThroughScalarUnwrapping()
    {
        var collection = JsonSerializer.SerializeToElement(new[] { "alpha", "beta" });
        var context = NewContext(collection);

        await ((ForEachActivity)context.Activity).OnChildCompletedAsync(NewCompletion(context, iterationIndex: 0));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        var item = Assert.IsType<ValueEnvelope>(request.IterationFrame!.Values[ForEachActivity.CurrentItemVariableName]);
        Assert.Equal("beta", item.InlineValue!.Value.GetString());
        Assert.DoesNotContain(RuntimeMetadataKeys.LoopIterationItemValue, request.Metadata);
    }

    // The non-enumerable guard must fire on the completion path too, not only at Execute — the callback
    // re-reads the collection to resolve the next item.
    [Fact]
    public async Task OnChildCompleted_Throws_WhenCollectionIsNotEnumerable()
    {
        var context = NewContext(collection: 42);

        await Assert.ThrowsAsync<ForEachExecutionException>(() =>
            ((ForEachActivity)context.Activity).OnChildCompletedAsync(NewCompletion(context, iterationIndex: 0)).AsTask());
    }

    // A string collection input is rejected on the completion path with the same guard as Execute.
    [Fact]
    public async Task OnChildCompleted_Throws_WhenCollectionIsString()
    {
        var context = NewContext(collection: "not-a-list");

        await Assert.ThrowsAsync<ForEachExecutionException>(() =>
            ((ForEachActivity)context.Activity).OnChildCompletedAsync(NewCompletion(context, iterationIndex: 0)).AsTask());
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenCompletedChildIsNotTheBody()
    {
        var context = NewContext(new[] { "x" });

        await Assert.ThrowsAsync<ForEachExecutionException>(() => new ForEachActivity()
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-x", "node-other", [ActivityOutcomes.Done], $"{ForEachExecutionId}:foreach-iteration:0"))
            .AsTask());
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenCompletedChildCarriesNoIterationId()
    {
        var context = NewContext(new[] { "x", "y" });

        await Assert.ThrowsAsync<ForEachExecutionException>(() => new ForEachActivity()
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-body", BodyNodeId, [ActivityOutcomes.Done]))
            .AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenRuntimeContextIsMissing()
    {
        await Assert.ThrowsAsync<ForEachExecutionException>(() => ((IActivity)new ForEachActivity())
            .ExecuteAsync(new NonRuntimeActivityExecutionContext(_serviceProvider, new ForEachActivity()))
            .AsTask());
    }

    private static async ValueTask ExecuteAsync(SimpleActivityExecutionContext context) =>
        await ((IActivity)context.Activity).ExecuteAsync(context);

    private static ActivityChildCompletedContext NewCompletion(SimpleActivityExecutionContext context, int iterationIndex) =>
        new(context, "actexec-body", BodyNodeId, [ActivityOutcomes.Done], $"{ForEachExecutionId}:foreach-iteration:{iterationIndex}");

    private SimpleActivityExecutionContext NewContext(object? collection, RuntimeInputBinding? collectionBinding = null)
    {
        var activity = new ForEachActivity { Id = ForEachExecutionId, NodeId = ForEachNodeId, Collection = collection };
        var context = new SimpleActivityExecutionContext(
            _serviceProvider,
            activity,
            CancellationToken.None,
            "wfexec-1",
            NewIdentity(),
            NewWorkItem(),
            NewForEachNode(collectionBinding),
            NewRunningState());
        return context;
    }

    private static ExecutableNode NewForEachNode(RuntimeInputBinding? collectionBinding = null) =>
        new(
            executableNodeId: ForEachNodeId,
            authoredActivityId: "authored-foreach",
            activityType: typeof(ForEachActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: collectionBinding is null
                ? new Dictionary<string, RuntimeInputBinding>()
                : new Dictionary<string, RuntimeInputBinding> { [nameof(ForEachActivity.Collection)] = collectionBinding },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(ForEachActivity.BodySlotName, [NewBodyNode()])],
            structure: new ExecutableActivityStructure(
                ForEachActivity.StructureKind,
                ForEachActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = BodyNodeId })));

    private static RuntimeInputBinding CollectionBinding(ValueProtectionPolicy policy)
    {
        var type = new ValueTypeDescriptor("String", CollectionKind.List);
        return new RuntimeInputBinding(
            nameof(ForEachActivity.Collection),
            type,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(new[] { "value" }), policy));
    }

    private static ExecutableNode NewBodyNode() =>
        new(
            executableNodeId: BodyNodeId,
            authoredActivityId: "authored-body",
            activityType: "test/probe",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution(ForEachExecutionId, "wfexec-1", ForEachNodeId, "authored-foreach", "foreach", "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static RuntimeSchedulerWorkItem NewWorkItem() =>
        new(
            workItemId: "work-foreach",
            workflowExecutionId: "wfexec-1",
            commandId: "command-foreach",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:foreach",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class NonRuntimeActivityExecutionContext(IServiceProvider serviceProvider, IActivity activity) : IActivityExecutionContext
    {
        public IActivity Activity { get; } = activity;
        public IActivityExecutionContext ParentActivityExecutionContext => null!;
        public CancellationToken CancellationToken => CancellationToken.None;

        public TService GetRequiredService<TService>() where TService : notnull =>
            serviceProvider.GetRequiredService<TService>();

        public IAsyncEnumerable<ActivityOutputs> GetActivityOutputs() => AsyncEnumerable.Empty<ActivityOutputs>();
        public void SetOutcomes(string[] outcomes) { }
        public IEnumerable<string> GetOutcomes() => [];
        public void CreateBookmark(ActivityBookmarkRequest request) { }
        public IReadOnlyCollection<ActivityBookmarkRequest> GetBookmarkRequests() => [];
    }
}
