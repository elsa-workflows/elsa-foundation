using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

public sealed class WorkflowParentActivityCompletionSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowParentActivityCompletionSchedulerWorkHandler);
    private const string CompletionOutcomeNamesMetadataKey = "runtime.completionOutcomeNames";

    private readonly IRuntimeActivityInputMaterializer _inputMaterializer;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IWorkflowExecutionAmbientServicesAccessor _ambientServicesAccessor;
    private readonly TimeProvider _timeProvider;

    public WorkflowParentActivityCompletionSchedulerWorkHandler(
        IRuntimeActivityInputMaterializer inputMaterializer,
        IServiceScopeFactory serviceScopeFactory)
        : this(inputMaterializer, serviceScopeFactory, TimeProvider.System)
    {
    }

    public WorkflowParentActivityCompletionSchedulerWorkHandler(
        IRuntimeActivityInputMaterializer inputMaterializer,
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider)
        : this(inputMaterializer, serviceScopeFactory, NoopWorkflowExecutionAmbientServicesAccessor.Instance, timeProvider)
    {
    }

    public WorkflowParentActivityCompletionSchedulerWorkHandler(
        IRuntimeActivityInputMaterializer inputMaterializer,
        IServiceScopeFactory serviceScopeFactory,
        IWorkflowExecutionAmbientServicesAccessor ambientServicesAccessor,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inputMaterializer);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);
        ArgumentNullException.ThrowIfNull(ambientServicesAccessor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _inputMaterializer = inputMaterializer;
        _serviceScopeFactory = serviceScopeFactory;
        _ambientServicesAccessor = ambientServicesAccessor;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        if (workItem.CommandKind != WorkflowExecutionCommandKind.CompleteActivity)
            return false;

        if (workItem.Payload is not { } payload)
            return false;

        try
        {
            var completionPayload = payload.Deserialize<RuntimeCompleteActivityCommandPayload>();
            return completionPayload?.CompletionKind == SchedulerCompletionKind.ParentCompletionEvaluation;
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsCompletePayloadValidationException(argumentException))
        {
            return false;
        }
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = DeserializeCompletePayload(workItem);
        if (payload.CompletionKind != SchedulerCompletionKind.ParentCompletionEvaluation)
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' is not parent completion evaluation work.");

        if (_ambientServicesAccessor.Current is { } ambientServices)
        {
            await HandleWithServicesAsync(workItem, payload, ambientServices, cancellationToken);
            return;
        }

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        await HandleWithServicesAsync(workItem, payload, scope.ServiceProvider, cancellationToken);
    }

    private async ValueTask HandleWithServicesAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var workflowExecutableStore = serviceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var activityExecutionStateStore = serviceProvider.GetRequiredService<IActivityExecutionStateStore>();
        var schedulerWorkQueue = serviceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();

        var executable = await workflowExecutableStore.FindAsync(payload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(payload.PinnedExecutable.ArtifactId);

        ValidatePinnedExecutable(workItem, payload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.TryGetValue(payload.ExecutableNodeId, out var parentExecutableNode))
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references parent executable node '{payload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var parentState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken);
        if (parentState is null)
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references missing parent activity execution '{payload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (payload.CompletedChildActivityExecutionId is not { } completedChildActivityExecutionId)
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' requires a completed child activity execution ID.");

        var completedChildState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, completedChildActivityExecutionId, cancellationToken);
        if (completedChildState is null)
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references missing completed child activity execution '{completedChildActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!executable.NodesById.ContainsKey(completedChildState.Execution.ExecutableNodeId))
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references completed child executable node '{completedChildState.Execution.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        if (!StringComparer.Ordinal.Equals(parentState.Execution.ExecutableNodeId, payload.ExecutableNodeId))
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references executable node '{payload.ExecutableNodeId}', but parent activity execution '{payload.ActivityExecutionId}' belongs to executable node '{parentState.Execution.ExecutableNodeId}'.");

        if (parentState.Status != ActivityExecutionStatus.Running)
            return;

        var activityFactory = serviceProvider.GetRequiredService<IActivityFactory>();
        var activityOutputRegister = serviceProvider.GetRequiredService<IRuntimeActivityOutputRegister>();
        var durableValueStateStore = serviceProvider.GetRequiredService<IDurableValueStateStore>();
        var idGenerator = serviceProvider.GetRequiredService<IRuntimeExecutionIdGenerator>();

        var constructedParent = await ConstructActivityAsync(
            activityFactory,
            activityOutputRegister,
            durableValueStateStore,
            workItem,
            payload,
            parentExecutableNode,
            cancellationToken);
        var parentActivity = constructedParent.Activity;

        if (parentActivity is not IActivityChildCompletionHandler childCompletionHandler)
        {
            await EnqueueContinuationSchedulingAsync(schedulerWorkQueue, workItem, payload, cancellationToken);
            return;
        }

        parentActivity.NodeId = parentExecutableNode.ExecutableNodeId;
        parentActivity.Id = payload.ActivityExecutionId;

        var context = new SimpleActivityExecutionContext(
            serviceProvider,
            parentActivity,
            cancellationToken,
            workItem.WorkflowExecutionId,
            payload.PinnedExecutable,
            workItem,
            parentExecutableNode,
            parentState);
        RuntimeActivityInputMemory.Seed(context, constructedParent.Inputs);

        var childCompletedContext = new ActivityChildCompletedContext(
            context,
            completedChildActivityExecutionId,
            completedChildState.Execution.ExecutableNodeId,
            payload.OutcomeNames);

        await childCompletionHandler.OnChildCompletedAsync(childCompletedContext);

        var childScheduleRequests = context.GetChildActivityScheduleRequests();
        if (context.CompositeCompletionRequested && childScheduleRequests.Count > 0)
            throw new InvalidOperationException("Composite activity cannot both request completion and schedule child activities in the same child-completion evaluation.");

        if (childScheduleRequests.Count > 0)
        {
            await EnqueueChildActivityScheduleWorkAsync(schedulerWorkQueue, idGenerator, workItem, payload, childScheduleRequests, cancellationToken);
            return;
        }

        if (!context.CompositeCompletionRequested)
            throw new InvalidOperationException($"Composite activity execution '{payload.ActivityExecutionId}' did not request completion or child activity scheduling after child execution '{completedChildActivityExecutionId}' completed.");

        var completedParentState = CompleteParentActivity(workItem, payload, parentState, context.CompositeCompletionOutcomeNames);
        await activityExecutionStateStore.SaveAsync(completedParentState, cancellationToken);
        await EnqueueCompletionWorkAsync(schedulerWorkQueue, workItem, payload, completedParentState, cancellationToken);
    }

    private async ValueTask<ConstructedActivity> ConstructActivityAsync(
        IActivityFactory activityFactory,
        IRuntimeActivityOutputRegister activityOutputRegister,
        IDurableValueStateStore durableValueStateStore,
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        ExecutableNode executableNode,
        CancellationToken cancellationToken)
    {
        var durableValues = await durableValueStateStore.ListAsync(workItem.WorkflowExecutionId, cancellationToken);
        var resolutionContext = new RuntimeInputBindingResolutionContext(
            workflowExecutionId: workItem.WorkflowExecutionId,
            activityExecutionId: payload.ActivityExecutionId,
            durableValuesByValueId: durableValues.ToDictionary(value => value.ValueId, StringComparer.Ordinal),
            activityOutputs: activityOutputRegister);
        var inputs = _inputMaterializer.MaterializeInputs(executableNode, resolutionContext);

        var activity = await activityFactory.Create(
            executableNode.DescriptorType,
            executableNode.DescriptorPayload,
            inputs.ToDictionary(input => input.Name, input => input.Argument, StringComparer.OrdinalIgnoreCase),
            BuildOutputArguments(executableNode),
            cancellationToken);

        return new ConstructedActivity(activity, inputs);
    }

    private async ValueTask EnqueueChildActivityScheduleWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem parentCompletionWorkItem,
        RuntimeCompleteActivityCommandPayload parentCompletionPayload,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
        CancellationToken cancellationToken)
    {
        var requests = scheduleRequests.ToArray();
        if (requests.Select(request => request.ExecutableNodeId).Distinct(StringComparer.Ordinal).Count() != requests.Length)
            throw new InvalidOperationException("Child activity schedule requests cannot contain duplicate executable node IDs.");

        for (var index = 0; index < requests.Length; index++)
        {
            var request = requests[index];
            var now = _timeProvider.GetUtcNow();
            var childActivityExecutionId = idGenerator.NewActivityExecutionId();
            var payload = new RuntimeScheduleActivityCommandPayload(
                parentCompletionPayload.PinnedExecutable,
                request.ExecutableNodeId,
                childActivityExecutionId,
                RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                request.SchedulingActivityExecutionId ?? parentCompletionPayload.ActivityExecutionId,
                parentCompletionPayload.ActivityExecutionId);

            var commandMetadata = parentCompletionWorkItem.CommandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            foreach (var item in request.Metadata)
                commandMetadata[item.Key] = item.Value;

            commandMetadata["runtime.parentActivityExecutionId"] = parentCompletionPayload.ActivityExecutionId;
            commandMetadata["runtime.childExecutableNodeId"] = request.ExecutableNodeId;

            var workItem = new RuntimeSchedulerWorkItem(
                workItemId: $"{parentCompletionWorkItem.WorkItemId}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                workflowExecutionId: parentCompletionWorkItem.WorkflowExecutionId,
                commandId: $"{parentCompletionWorkItem.CommandId}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
                envelopeId: parentCompletionWorkItem.EnvelopeId,
                idempotencyKey: $"{parentCompletionWorkItem.IdempotencyKey}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                enqueuedAt: now,
                recordedAt: now,
                sequence: parentCompletionWorkItem.Sequence is { } sequence ? sequence + index + 1 : null,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: commandMetadata,
                envelopeMetadata: parentCompletionWorkItem.EnvelopeMetadata);

            await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
        }
    }

    private async ValueTask EnqueueCompletionWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem parentCompletionWorkItem,
        RuntimeCompleteActivityCommandPayload parentCompletionPayload,
        ActivityExecutionState completedParentState,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeCompleteActivityCommandPayload(
            parentCompletionPayload.PinnedExecutable,
            parentCompletionPayload.ExecutableNodeId,
            parentCompletionPayload.ActivityExecutionId,
            completedParentState.ParentActivityExecutionId,
            completedParentState.BranchId,
            ReadCompletionOutcomeNames(completedParentState),
            RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason);

        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: $"{parentCompletionWorkItem.WorkItemId}:complete:{parentCompletionPayload.ActivityExecutionId}",
            workflowExecutionId: parentCompletionWorkItem.WorkflowExecutionId,
            commandId: $"{parentCompletionWorkItem.CommandId}:complete:{parentCompletionPayload.ActivityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: parentCompletionWorkItem.EnvelopeId,
            idempotencyKey: $"{parentCompletionWorkItem.IdempotencyKey}:complete:{parentCompletionPayload.ActivityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: parentCompletionWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: parentCompletionWorkItem.CommandMetadata,
            envelopeMetadata: parentCompletionWorkItem.EnvelopeMetadata);

        await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private async ValueTask EnqueueContinuationSchedulingAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem sourceWorkItem,
        RuntimeCompleteActivityCommandPayload sourcePayload,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var activityExecutionId = sourcePayload.ActivityExecutionId;
        var payload = new RuntimeCompleteActivityCommandPayload(
            sourcePayload.PinnedExecutable,
            sourcePayload.ExecutableNodeId,
            activityExecutionId,
            sourcePayload.ParentActivityExecutionId,
            sourcePayload.BranchId,
            sourcePayload.OutcomeNames,
            RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason,
            SchedulerCompletionKind.ContinuationScheduling);

        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: $"{sourceWorkItem.WorkItemId}:continuation:{activityExecutionId}",
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            commandId: $"{sourceWorkItem.CommandId}:continuation:{activityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: sourceWorkItem.EnvelopeId,
            idempotencyKey: $"{sourceWorkItem.IdempotencyKey}:continuation:{activityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: sourceWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: sourceWorkItem.CommandMetadata,
            envelopeMetadata: sourceWorkItem.EnvelopeMetadata);

        await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private ActivityExecutionState CompleteParentActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        ActivityExecutionState state,
        IReadOnlyCollection<string> outcomeNames)
    {
        var normalizedOutcomeNames = NormalizeOutcomeNames(outcomeNames, defaultToDone: true);
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["runtime.invokeReason"] = payload.Reason;
        metadata["runtime.invokeSchedulerWorkItemId"] = workItem.WorkItemId;
        metadata[CompletionOutcomeNamesMetadataKey] = JsonSerializer.Serialize(normalizedOutcomeNames);

        return state with
        {
            Status = ActivityExecutionStatus.Completed,
            CompletedAt = _timeProvider.GetUtcNow(),
            Metadata = metadata
        };
    }

    private static RuntimeCompleteActivityCommandPayload DeserializeCompletePayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("CompleteActivity scheduler work item requires a complete activity payload.");

        try
        {
            return payload.Deserialize<RuntimeCompleteActivityCommandPayload>()
                   ?? throw new InvalidOperationException("CompleteActivity scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsCompletePayloadValidationException(argumentException))
        {
            throw new InvalidOperationException("CompleteActivity scheduler work item payload is not a valid complete activity payload.", exception);
        }
    }

    private static bool IsCompletePayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "parentActivityExecutionId" or
            "branchId" or
            "outcomeNames" or
            "reason" or
            "completionKind" or
            "completedChildActivityExecutionId";

    private static IDictionary<string, OutputArgument> BuildOutputArguments(ExecutableNode executableNode) =>
        executableNode.OutputCaptures.ToDictionary(
            item => item.Key,
            item => (OutputArgument)new OutputArgument<object?>(new RuntimeOutputMemoryBlockReference(item.Key)),
            StringComparer.Ordinal);

    private static IReadOnlyCollection<string> ReadCompletionOutcomeNames(ActivityExecutionState completedState)
    {
        if (completedState.Metadata.TryGetValue(CompletionOutcomeNamesMetadataKey, out var serializedOutcomeNames))
        {
            var outcomeNames = JsonSerializer.Deserialize<string[]>(serializedOutcomeNames)
                ?? throw new InvalidOperationException("Persisted completion outcome names resolved to null.");

            return NormalizeOutcomeNames(outcomeNames, defaultToDone: false);
        }

        return [ActivityOutcomes.Done];
    }

    private static IReadOnlyCollection<string> NormalizeOutcomeNames(IEnumerable<string> outcomeNames, bool defaultToDone)
    {
        var snapshot = outcomeNames.ToArray();
        if (snapshot.Length == 0)
            return defaultToDone ? [ActivityOutcomes.Done] : [];

        if (snapshot.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Activity completion outcome names cannot contain blank values.");

        if (snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new InvalidOperationException("Activity completion outcome names cannot contain duplicates.");

        return snapshot;
    }

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"CompleteActivity scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

    private sealed class RuntimeOutputMemoryBlockReference(string id) : IMemoryBlockReference
    {
        public string Id { get; set; } = id;

        public IMemoryBlock Declare() => new RuntimeOutputMemoryBlock();

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) =>
            context.Get<T>(this);

        public T? Get<T>(IExpressionExecutionContext context) =>
            context.Get<T>(this);
    }

    private sealed class RuntimeOutputMemoryBlock : IMemoryBlock
    {
        public object? Value { get; set; }
        public object? Metadata { get; set; }
    }

    private sealed record ConstructedActivity(
        IActivity Activity,
        IReadOnlyList<RuntimeMaterializedActivityInput> Inputs);
}
