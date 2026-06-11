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

public sealed class WorkflowInvokeActivitySchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowInvokeActivitySchedulerWorkHandler);
    private const string SkippedSubStatus = "Skipped";
    private const string CompletionOutcomeNamesMetadataKey = "runtime.completionOutcomeNames";

    private readonly IRuntimeActivityInputMaterializer _inputMaterializer;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeProvider _timeProvider;

    public WorkflowInvokeActivitySchedulerWorkHandler(
        IRuntimeActivityInputMaterializer inputMaterializer,
        IServiceScopeFactory serviceScopeFactory)
        : this(inputMaterializer, serviceScopeFactory, TimeProvider.System)
    {
    }

    public WorkflowInvokeActivitySchedulerWorkHandler(
        IRuntimeActivityInputMaterializer inputMaterializer,
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inputMaterializer);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _inputMaterializer = inputMaterializer;
        _serviceScopeFactory = serviceScopeFactory;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.InvokeActivity;
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var invokePayload = DeserializeInvokePayload(workItem);
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var workflowExecutableStore = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var activityExecutionStateStore = scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>();
        var activityFactory = scope.ServiceProvider.GetRequiredService<IActivityFactory>();
        var schedulerWorkQueue = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();

        var executable = await workflowExecutableStore.FindAsync(invokePayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(invokePayload.PinnedExecutable.ArtifactId);

        ValidatePinnedExecutable(workItem, invokePayload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.TryGetValue(invokePayload.ExecutableNodeId, out var executableNode))
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' references executable node '{invokePayload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var state = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId, cancellationToken);
        if (state is null)
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' references missing activity execution '{invokePayload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, invokePayload.ExecutableNodeId))
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' references executable node '{invokePayload.ExecutableNodeId}', but activity execution '{invokePayload.ActivityExecutionId}' belongs to executable node '{state.Execution.ExecutableNodeId}'.");

        if (state.Status == ActivityExecutionStatus.Completed)
        {
            await EnqueueCompletionWorkAsync(schedulerWorkQueue, workItem, invokePayload, state, cancellationToken);
            return;
        }

        if (state.Status != ActivityExecutionStatus.Running)
            return;

        var activityOutputRegister = scope.ServiceProvider.GetRequiredService<IRuntimeActivityOutputRegister>();
        var durableValueStateStore = scope.ServiceProvider.GetRequiredService<IDurableValueStateStore>();
        var checkpointCommitter = scope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>();
        var activityFaultIncidentRecorder = scope.ServiceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        await InvokeActivityAsync(scope.ServiceProvider, activityFactory, activityExecutionStateStore, schedulerWorkQueue, activityOutputRegister, durableValueStateStore, checkpointCommitter, activityFaultIncidentRecorder, workItem, invokePayload, executableNode, state, cancellationToken);
    }

    private async ValueTask InvokeActivityAsync(
        IServiceProvider serviceProvider,
        IActivityFactory activityFactory,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IRuntimeActivityOutputRegister activityOutputRegister,
        IDurableValueStateStore durableValueStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        ActivityFaultIncidentRecorder activityFaultIncidentRecorder,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RuntimeMaterializedActivityInput> inputs;
        try
        {
            var durableValues = await durableValueStateStore.ListAsync(workItem.WorkflowExecutionId, cancellationToken);
            var resolutionContext = new RuntimeInputBindingResolutionContext(
                workflowExecutionId: workItem.WorkflowExecutionId,
                activityExecutionId: invokePayload.ActivityExecutionId,
                durableValuesByValueId: durableValues.ToDictionary(value => value.ValueId, StringComparer.Ordinal),
                activityOutputs: activityOutputRegister);
            inputs = _inputMaterializer.MaterializeInputs(executableNode, resolutionContext);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await activityFaultIncidentRecorder.CommitAsync(NewFaultIncidentRecordRequest(checkpointCommitter, workItem, invokePayload, state, exception, "InputMaterializationFailed"), cancellationToken);
            return;
        }

        var activity = await activityFactory.Create(
            executableNode.DescriptorType,
            executableNode.DescriptorPayload,
            inputs.ToDictionary(input => input.Name, input => input.Argument, StringComparer.OrdinalIgnoreCase),
            BuildOutputArguments(executableNode),
            cancellationToken);

        activity.NodeId = executableNode.ExecutableNodeId;
        activity.Id = invokePayload.ActivityExecutionId;

        var context = new SimpleActivityExecutionContext(serviceProvider, activity, cancellationToken);
        RuntimeActivityInputMemory.Seed(context, inputs);

        ActivityExecutionState completedState;
        try
        {
            if (!await activity.CanExecuteAsync(context))
            {
                completedState = CompleteActivity(workItem, invokePayload, state, outcomeNames: [], skipped: true);
            }
            else
            {
                await activity.ExecuteAsync(context);
                var bookmarkRequests = context.GetBookmarkRequests();
                if (bookmarkRequests.Count > 0)
                {
                    await EnqueueBookmarkCreationWorkAsync(schedulerWorkQueue, workItem, invokePayload, bookmarkRequests, cancellationToken);
                    return;
                }

                var recordedOutputs = context.GetRecordedOutputs();
                if (recordedOutputs.Count > 0)
                {
                    var recordedAt = _timeProvider.GetUtcNow();
                    PublishActivityOutputs(activityOutputRegister, workItem, invokePayload, executableNode, recordedOutputs, recordedAt);
                    await CaptureDurableOutputsAsync(checkpointCommitter, workItem, invokePayload, executableNode, recordedOutputs, recordedAt, cancellationToken);
                }

                completedState = CompleteActivity(workItem, invokePayload, state, NormalizeOutcomeNames(context.GetOutcomes(), defaultToDone: true), skipped: false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await activityFaultIncidentRecorder.CommitAsync(NewFaultIncidentRecordRequest(checkpointCommitter, workItem, invokePayload, state, exception, "ActivityFaulted"), cancellationToken);
            return;
        }

        await activityExecutionStateStore.SaveAsync(completedState, cancellationToken);
        await EnqueueCompletionWorkAsync(schedulerWorkQueue, workItem, invokePayload, completedState, cancellationToken);
    }

    private static IDictionary<string, OutputArgument> BuildOutputArguments(ExecutableNode executableNode) =>
        executableNode.OutputCaptures.ToDictionary(
            item => item.Key,
            item => (OutputArgument)new OutputArgument<object?>(new RuntimeOutputMemoryBlockReference(item.Key)),
            StringComparer.Ordinal);

    private static void PublishActivityOutputs(
        IRuntimeActivityOutputRegister activityOutputRegister,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ExecutableNode executableNode,
        IReadOnlyCollection<RecordedActivityOutput> outputs,
        DateTimeOffset recordedAt)
    {
        foreach (var output in outputs)
        {
            executableNode.OutputCaptures.TryGetValue(output.OutputName, out var capture);
            var metadata = new Dictionary<string, string>
            {
                ["runtime.executableNodeId"] = invokePayload.ExecutableNodeId,
                ["runtime.invokeSchedulerWorkItemId"] = workItem.WorkItemId
            };

            activityOutputRegister.Set(new ActiveActivityOutput(
                key: new ActiveActivityOutputKey(workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId, output.OutputName),
                value: SerializeOutputValue(output.Value),
                type: capture?.Type,
                recordedAt: recordedAt,
                metadata: metadata));
        }
    }

    private async ValueTask CaptureDurableOutputsAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ExecutableNode executableNode,
        IReadOnlyCollection<RecordedActivityOutput> outputs,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        var outputByName = outputs.ToDictionary(output => output.OutputName, StringComparer.Ordinal);
        var capturedValues = executableNode.OutputCaptures.Values
            .Where(capture => capture.CaptureOnSuccessfulCompletion)
            .Where(capture => outputByName.ContainsKey(capture.OutputName))
            .Select(capture => NewDurableValueChange(workItem, invokePayload, capture, outputByName[capture.OutputName], capturedAt))
            .ToArray();

        if (capturedValues.Length == 0)
            return;

        var metadata = new Dictionary<string, string>
        {
            ["runtime.schedulerWorkItemId"] = workItem.WorkItemId,
            ["runtime.commandId"] = workItem.CommandId,
            ["runtime.activityExecutionId"] = invokePayload.ActivityExecutionId,
            ["runtime.executableNodeId"] = invokePayload.ExecutableNodeId
        };
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:durable-values-captured:{invokePayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{workItem.WorkItemId}:durable-values-captured:{invokePayload.ActivityExecutionId}",
                Name: RuntimeCheckpointNames.DurableValueCaptured,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: capturedAt,
                ActivityExecutionIds: [invokePayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: capturedValues,
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private static RuntimeStateChange<DurableValueState> NewDurableValueChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        RuntimeOutputCapture capture,
        RecordedActivityOutput output,
        DateTimeOffset capturedAt)
    {
        if (capture.Storage == DurableValueStorage.External)
            throw new InvalidOperationException($"Output capture '{capture.OutputName}' targets external durable value storage, which is not implemented by the runtime invocation handler.");

        var metadata = capture.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["runtime.schedulerWorkItemId"] = workItem.WorkItemId;
        metadata["runtime.commandId"] = workItem.CommandId;
        metadata["runtime.outputName"] = capture.OutputName;
        metadata["runtime.activityExecutionId"] = invokePayload.ActivityExecutionId;
        metadata["runtime.executableNodeId"] = invokePayload.ExecutableNodeId;
        var durableValueId = $"durable-{capture.ValueId}";
        var state = new DurableValueState(
            durableValueId: durableValueId,
            workflowExecutionId: workItem.WorkflowExecutionId,
            valueId: capture.ValueId,
            type: capture.Type,
            lifecycle: capture.Lifecycle,
            storage: capture.Storage,
            inlineValue: capture.Storage is DurableValueStorage.Inline or DurableValueStorage.Custom ? SerializeOutputValue(output.Value) : null,
            externalReference: null,
            sourceActivityExecutionId: invokePayload.ActivityExecutionId,
            capturedAt: capturedAt,
            metadata: metadata);

        return new RuntimeStateChange<DurableValueState>(
            StateId: durableValueId,
            Operation: RuntimeStateChangeOperation.Upsert,
            State: state,
            Metadata: metadata);
    }

    private static JsonElement SerializeOutputValue(object? value) =>
        value is JsonElement json
            ? json.Clone()
            : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));

    private static ActivityFaultIncidentRecordRequest NewFaultIncidentRecordRequest(
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus)
    {
        var activityMetadata = new Dictionary<string, string>
        {
            ["runtime.invokeReason"] = invokePayload.Reason,
            ["runtime.invokeSchedulerWorkItemId"] = workItem.WorkItemId
        };

        return new ActivityFaultIncidentRecordRequest(
            CheckpointCommitter: checkpointCommitter,
            WorkItem: workItem,
            ActivityExecutionId: invokePayload.ActivityExecutionId,
            ExecutableNodeId: invokePayload.ExecutableNodeId,
            State: state,
            Exception: exception,
            SubStatus: subStatus,
            ActivityMetadata: activityMetadata,
            IncidentMetadata: new Dictionary<string, string>());
    }

    private async ValueTask EnqueueBookmarkCreationWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IReadOnlyCollection<ActivityBookmarkRequest> bookmarkRequests,
        CancellationToken cancellationToken)
    {
        var requests = bookmarkRequests.ToArray();
        if (requests.Select(request => request.BookmarkId).Distinct(StringComparer.Ordinal).Count() != requests.Length)
            throw new InvalidOperationException("Activity bookmark requests cannot contain duplicate bookmark IDs.");

        for (var index = 0; index < requests.Length; index++)
        {
            var request = requests[index];
            var now = _timeProvider.GetUtcNow();
            var payload = new RuntimeCreateBookmarkCommandPayload(
                pinnedExecutable: invokePayload.PinnedExecutable,
                bookmarkId: request.BookmarkId,
                activityExecutionId: invokePayload.ActivityExecutionId,
                executableNodeId: invokePayload.ExecutableNodeId,
                resumeTargetId: request.ResumeTargetId,
                stimulusType: request.StimulusType,
                stimulusHash: request.StimulusHash,
                payload: request.Payload,
                expiresAt: request.ExpiresAt,
                reason: RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason,
                metadata: request.Metadata);

            var workItem = new RuntimeSchedulerWorkItem(
                workItemId: $"{invokeWorkItem.WorkItemId}:create-bookmark:{request.BookmarkId}",
                workflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                commandId: $"{invokeWorkItem.CommandId}:create-bookmark:{request.BookmarkId}",
                commandKind: WorkflowExecutionCommandKind.CreateBookmark,
                envelopeId: invokeWorkItem.EnvelopeId,
                idempotencyKey: $"{invokeWorkItem.IdempotencyKey}:create-bookmark:{request.BookmarkId}",
                enqueuedAt: now,
                recordedAt: now,
                sequence: invokeWorkItem.Sequence is { } sequence ? sequence + index + 1 : null,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: MergeMetadata(invokeWorkItem.CommandMetadata, request),
                envelopeMetadata: invokeWorkItem.EnvelopeMetadata);

            await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
        }
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string> commandMetadata,
        ActivityBookmarkRequest request)
    {
        var metadata = commandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in request.Metadata)
            metadata[item.Key] = item.Value;

        metadata["runtime.bookmarkId"] = request.BookmarkId;
        metadata["runtime.resumeTargetId"] = request.ResumeTargetId;
        metadata["runtime.stimulusType"] = request.StimulusType;
        metadata["runtime.stimulusHash"] = request.StimulusHash;
        return metadata;
    }

    private async ValueTask EnqueueCompletionWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState completedState,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeCompleteActivityCommandPayload(
            invokePayload.PinnedExecutable,
            invokePayload.ExecutableNodeId,
            invokePayload.ActivityExecutionId,
            completedState.ParentActivityExecutionId,
            completedState.BranchId,
            ReadCompletionOutcomeNames(completedState),
            RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason);

        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: $"{invokeWorkItem.WorkItemId}:complete:{invokePayload.ActivityExecutionId}",
            workflowExecutionId: invokeWorkItem.WorkflowExecutionId,
            commandId: $"{invokeWorkItem.CommandId}:complete:{invokePayload.ActivityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: invokeWorkItem.EnvelopeId,
            idempotencyKey: $"{invokeWorkItem.IdempotencyKey}:complete:{invokePayload.ActivityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: invokeWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: invokeWorkItem.CommandMetadata,
            envelopeMetadata: invokeWorkItem.EnvelopeMetadata);

        await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private static RuntimeInvokeActivityCommandPayload DeserializeInvokePayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("InvokeActivity scheduler work item requires an invoke activity payload.");

        try
        {
            return payload.Deserialize<RuntimeInvokeActivityCommandPayload>()
                   ?? throw new InvalidOperationException("InvokeActivity scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsInvokePayloadValidationException(argumentException))
        {
            throw new InvalidOperationException("InvokeActivity scheduler work item payload is not a valid invoke activity payload.", exception);
        }
    }

    private static bool IsInvokePayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "reason";

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"InvokeActivity scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

    private ActivityExecutionState CompleteActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState state,
        IReadOnlyCollection<string> outcomeNames,
        bool skipped)
    {
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["runtime.invokeReason"] = invokePayload.Reason;
        metadata["runtime.invokeSchedulerWorkItemId"] = workItem.WorkItemId;
        metadata[CompletionOutcomeNamesMetadataKey] = JsonSerializer.Serialize(outcomeNames);

        if (skipped)
            metadata["runtime.invokeSkipped"] = bool.TrueString;

        return state with
        {
            Status = ActivityExecutionStatus.Completed,
            SubStatus = skipped ? SkippedSubStatus : null,
            CompletedAt = _timeProvider.GetUtcNow(),
            Metadata = metadata
        };
    }

    private static IReadOnlyCollection<string> ReadCompletionOutcomeNames(ActivityExecutionState completedState)
    {
        if (completedState.Metadata.TryGetValue(CompletionOutcomeNamesMetadataKey, out var serializedOutcomeNames))
        {
            var outcomeNames = JsonSerializer.Deserialize<string[]>(serializedOutcomeNames)
                ?? throw new InvalidOperationException("Persisted completion outcome names resolved to null.");

            return NormalizeOutcomeNames(outcomeNames, defaultToDone: false);
        }

        return completedState.SubStatus == SkippedSubStatus ? [] : [ActivityOutcomes.Done];
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
}
