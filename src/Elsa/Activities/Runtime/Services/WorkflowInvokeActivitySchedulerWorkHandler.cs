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

    private readonly IRuntimeActivityInputMaterializer _inputMaterializer;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IWorkflowExecutionAmbientServicesAccessor _ambientServicesAccessor;
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
        : this(inputMaterializer, serviceScopeFactory, NoopWorkflowExecutionAmbientServicesAccessor.Instance, timeProvider)
    {
    }

    public WorkflowInvokeActivitySchedulerWorkHandler(
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

        return workItem.CommandKind == WorkflowExecutionCommandKind.InvokeActivity;
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var invokePayload = DeserializeInvokePayload(workItem);
        if (_ambientServicesAccessor.Current is { } ambientServices)
        {
            await HandleWithServicesAsync(workItem, invokePayload, ambientServices, cancellationToken);
            return;
        }

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        await HandleWithServicesAsync(workItem, invokePayload, scope.ServiceProvider, cancellationToken);
    }

    private async ValueTask HandleWithServicesAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var workflowExecutableStore = serviceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var activityExecutionStateStore = serviceProvider.GetRequiredService<IActivityExecutionStateStore>();
        var activityFactory = serviceProvider.GetRequiredService<IActivityFactory>();
        var schedulerWorkQueue = serviceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();

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

        var activityOutputRegister = serviceProvider.GetRequiredService<IRuntimeActivityOutputRegister>();
        var durableValueStateStore = serviceProvider.GetRequiredService<IDurableValueStateStore>();
        var checkpointCommitter = serviceProvider.GetRequiredService<RuntimeCheckpointCommitter>();
        var activityFaultIncidentRecorder = serviceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        var inspectionAccumulator = serviceProvider.GetService<IRuntimeActivityExecutionInspectionAccumulator>();
        var payloadCapturePolicy = serviceProvider.GetService<IRuntimePayloadCapturePolicy>() ?? new DefaultRuntimePayloadCapturePolicy();
        await InvokeActivityAsync(serviceProvider, activityFactory, activityExecutionStateStore, schedulerWorkQueue, activityOutputRegister, durableValueStateStore, checkpointCommitter, activityFaultIncidentRecorder, inspectionAccumulator, payloadCapturePolicy, workItem, invokePayload, executable, executableNode, state, cancellationToken);
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
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        var scopeService = new RuntimeContainerScopeService(activityExecutionStateStore);
        var variableScope = await scopeService.BuildScopeAsync(executable, workItem.WorkflowExecutionId, state, cancellationToken);

        IReadOnlyList<RuntimeMaterializedActivityInput> inputs;
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges = [];
        try
        {
            var durableValues = await durableValueStateStore.ListAsync(workItem.WorkflowExecutionId, cancellationToken);
            var resolutionContext = new RuntimeInputBindingResolutionContext(
                workflowExecutionId: workItem.WorkflowExecutionId,
                activityExecutionId: invokePayload.ActivityExecutionId,
                durableValuesByValueId: durableValues.ToDictionary(value => value.ValueId, StringComparer.Ordinal),
                activityOutputs: activityOutputRegister,
                serviceProvider: serviceProvider,
                activityOutputValues: RuntimeInputBindingStateProjection.ProjectActivityOutputValues(durableValues),
                variableScope: variableScope);
            inputs = await _inputMaterializer.MaterializeInputsAsync(executableNode, resolutionContext, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await activityFaultIncidentRecorder.CommitAsync(NewFaultIncidentRecordRequest(checkpointCommitter, workItem, invokePayload, state, exception, "InputMaterializationFailed", []), cancellationToken);
            return;
        }
        var valueSnapshots = BuildInputValueSnapshots(payloadCapturePolicy, workItem, invokePayload, inputs, _timeProvider.GetUtcNow()).ToList();

        var activity = await activityFactory.Create(
            executableNode.DescriptorType,
            executableNode.DescriptorPayload,
            inputs.ToDictionary(input => input.Name, input => input.Argument, StringComparer.OrdinalIgnoreCase),
            BuildOutputArguments(executableNode),
            cancellationToken);

        activity.NodeId = executableNode.ExecutableNodeId;
        activity.Id = invokePayload.ActivityExecutionId;

        var context = new SimpleActivityExecutionContext(
            serviceProvider,
            activity,
            cancellationToken,
            workItem.WorkflowExecutionId,
            invokePayload.PinnedExecutable,
            workItem,
            executableNode,
            state,
            variableScope);
        RuntimeActivityInputMemory.Seed(context, inputs);

        ActivityExecutionState? completedState = null;
        (IRuntimeExecutionIdGenerator IdGenerator, IReadOnlyCollection<RuntimeChildActivityScheduleRequest> Requests)? pendingChildScheduling = null;
        try
        {
            if (!await activity.CanExecuteAsync(context))
            {
                completedState = CompleteActivity(workItem, invokePayload, state, outcomeNames: [], skipped: true);
            }
            else
            {
                await activity.ExecuteAsync(context);

                // Persist any container-scoped variable assignments the activity made back to the owning
                // container executions' snapshots, so sibling branches and later activities observe them
                // and resume restores them (ADR 0027).
                await scopeService.PersistScopeMutationsAsync(variableScope, workItem.WorkflowExecutionId, cancellationToken);

                var bookmarkRequests = context.GetBookmarkRequests();
                var childScheduleRequests = context.GetChildActivityScheduleRequests();
                if (context.CompositeCompletionRequested && childScheduleRequests.Count > 0)
                    throw new InvalidOperationException("Activity cannot both request composite completion and schedule child activities in the same execution.");

                if (bookmarkRequests.Count > 0 && childScheduleRequests.Count > 0)
                    throw new InvalidOperationException("Activity cannot both request durable bookmarks and schedule child activities in the same execution.");

                if (bookmarkRequests.Count > 0)
                {
                    await EnqueueBookmarkCreationWorkAsync(schedulerWorkQueue, workItem, invokePayload, bookmarkRequests, valueSnapshots, cancellationToken);
                    return;
                }

                if (childScheduleRequests.Count > 0)
                {
                    var idGenerator = serviceProvider.GetRequiredService<IRuntimeExecutionIdGenerator>();
                    if (inspectionAccumulator is null)
                    {
                        await EnqueueChildActivityScheduleWorkAsync(schedulerWorkQueue, idGenerator, workItem, invokePayload, childScheduleRequests, cancellationToken);
                        return;
                    }

                    pendingChildScheduling = (idGenerator, childScheduleRequests);
                }
                else
                {
                    var recordedOutputs = context.GetRecordedOutputs();
                    if (recordedOutputs.Count > 0)
                    {
                        var recordedAt = _timeProvider.GetUtcNow();
                        PublishActivityOutputs(activityOutputRegister, workItem, invokePayload, executableNode, recordedOutputs, recordedAt);
                        valueSnapshots.AddRange(BuildOutputValueSnapshots(payloadCapturePolicy, workItem, invokePayload, executableNode, recordedOutputs, recordedAt));
                        durableValueChanges = BuildDurableOutputChanges(workItem, invokePayload, executableNode, recordedOutputs, recordedAt);
                    }

                    var outcomeNames = context.CompositeCompletionRequested
                        ? NormalizeOutcomeNames(context.CompositeCompletionOutcomeNames, defaultToDone: true)
                        : NormalizeOutcomeNames(context.GetOutcomes(), defaultToDone: true);
                    completedState = CompleteActivity(workItem, invokePayload, state, outcomeNames, skipped: false);

                    // A completing container's scope is no longer live for runtime expressions; its
                    // final variable values are retained as inspection evidence only through the
                    // configured capture/retention policy (ADR 0027, #210).
                    if (context.CompositeCompletionRequested)
                    {
                        var containerVariableSnapshots = RuntimeContainerVariableEvidence.Capture(
                            payloadCapturePolicy, scopeService, executableNode, state,
                            workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId, workItem.WorkItemId, _timeProvider.GetUtcNow());
                        if (containerVariableSnapshots.Count > 0)
                        {
                            valueSnapshots.AddRange(containerVariableSnapshots);
                            completedState = RuntimeContainerScopeService.MarkScopeCompleted(completedState);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            valueSnapshots.AddRange(BuildOutputValueSnapshots(payloadCapturePolicy, workItem, invokePayload, executableNode, context.GetRecordedOutputs(), _timeProvider.GetUtcNow()));
            await activityFaultIncidentRecorder.CommitAsync(NewFaultIncidentRecordRequest(checkpointCommitter, workItem, invokePayload, state, exception, "ActivityFaulted", valueSnapshots), cancellationToken);
            return;
        }

        if (pendingChildScheduling is { } childScheduling)
        {
            await CommitChildSchedulingActivityAsync(checkpointCommitter, inspectionAccumulator!, childScheduling.IdGenerator, workItem, invokePayload, state, childScheduling.Requests, valueSnapshots, cancellationToken);
            return;
        }

        if (completedState is null)
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' did not produce a completion or child scheduling result for activity execution '{invokePayload.ActivityExecutionId}'.");

        if (inspectionAccumulator is null)
        {
            foreach (var change in durableValueChanges)
            {
                if (change.Operation != RuntimeStateChangeOperation.Upsert || change.State is null)
                    throw new InvalidOperationException($"Unsupported durable value change '{change.Operation}' while completing activity without checkpoint inspection.");

                await durableValueStateStore.SaveAsync(change.State, cancellationToken);
            }

            await activityExecutionStateStore.SaveAsync(completedState, cancellationToken);
            await EnqueueCompletionWorkAsync(schedulerWorkQueue, workItem, invokePayload, completedState, cancellationToken);
            return;
        }

        await CommitCompletedActivityAsync(checkpointCommitter, inspectionAccumulator, workItem, invokePayload, completedState, ReadCompletionOutcomeNames(completedState), valueSnapshots, durableValueChanges, cancellationToken);
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
                [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
                [RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId
            };

            activityOutputRegister.Set(new ActiveActivityOutput(
                key: new ActiveActivityOutputKey(workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId, output.OutputName),
                value: SerializeOutputValue(output.Value),
                type: capture?.Type,
                recordedAt: recordedAt,
                metadata: metadata));
        }
    }

    private static IReadOnlyCollection<RuntimeStateChange<DurableValueState>> BuildDurableOutputChanges(
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ExecutableNode executableNode,
        IReadOnlyCollection<RecordedActivityOutput> outputs,
        DateTimeOffset capturedAt)
    {
        var outputByName = outputs.ToDictionary(output => output.OutputName, StringComparer.Ordinal);
        return executableNode.OutputCaptures.Values
            .Where(capture => capture.CaptureOnSuccessfulCompletion)
            .Where(capture => outputByName.ContainsKey(capture.OutputName))
            .Select(capture => NewDurableValueChange(workItem, invokePayload, capture, outputByName[capture.OutputName], capturedAt))
            .ToArray();
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
        metadata[RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId;
        metadata[RuntimeMetadataKeys.CommandId] = workItem.CommandId;
        metadata[RuntimeMetadataKeys.OutputName] = capture.OutputName;
        metadata[RuntimeMetadataKeys.ActivityExecutionId] = invokePayload.ActivityExecutionId;
        metadata[RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId;
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

    private static JsonElement? SerializeCapturedValue(RuntimePayloadCaptureDecision decision, object? value) =>
        decision.CapturesPayload ? SerializeOutputValue(value) : null;

    private static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> BuildInputValueSnapshots(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IReadOnlyCollection<RuntimeMaterializedActivityInput> inputs,
        DateTimeOffset capturedAt) =>
        inputs
            .Select(input =>
            {
                var type = TypeDescriptorFor(input.Value);
                var decision = payloadCapturePolicy.Decide(new RuntimePayloadCaptureRequest(
                    RuntimePayloadCaptureSubject.ActivityInput,
                    workItem.WorkflowExecutionId,
                    capturedAt,
                    activityExecutionId: invokePayload.ActivityExecutionId,
                    valueName: input.Name,
                    type: type,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
                        [RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId
                    }));
                return ActivityExecutionInspectionValueSnapshot.FromDecision(
                    input.Name,
                    ActivityExecutionInspectionValueSubject.ActivityInput,
                    decision,
                    type,
                    capturedAt,
                    SerializeCapturedValue(decision, input.Value),
                    isSensitive: false,
                    metadata: decision.Metadata);
            })
            .ToArray();

    private static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> BuildOutputValueSnapshots(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ExecutableNode executableNode,
        IReadOnlyCollection<RecordedActivityOutput> outputs,
        DateTimeOffset capturedAt) =>
        outputs
            .Select(output =>
            {
                executableNode.OutputCaptures.TryGetValue(output.OutputName, out var capture);
                var type = capture?.Type ?? TypeDescriptorFor(output.Value);
                var decision = payloadCapturePolicy.Decide(new RuntimePayloadCaptureRequest(
                    RuntimePayloadCaptureSubject.ActivityOutput,
                    workItem.WorkflowExecutionId,
                    capturedAt,
                    activityExecutionId: invokePayload.ActivityExecutionId,
                    valueName: output.OutputName,
                    type: type,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
                        [RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId
                    }));
                return ActivityExecutionInspectionValueSnapshot.FromDecision(
                    output.OutputName,
                    ActivityExecutionInspectionValueSubject.ActivityOutput,
                    decision,
                    type,
                    capturedAt,
                    SerializeCapturedValue(decision, output.Value),
                    isSensitive: false,
                    metadata: decision.Metadata);
            })
            .ToArray();

    private static RuntimeValueTypeDescriptor RuntimeObjectType { get; } = new("clr", typeof(object).FullName, null);

    private static RuntimeValueTypeDescriptor TypeDescriptorFor(object? value) =>
        value is null ? RuntimeObjectType : new RuntimeValueTypeDescriptor("clr", value.GetType().FullName, null);

    private static ActivityFaultIncidentRecordRequest NewFaultIncidentRecordRequest(
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots)
    {
        var activityMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.InvokeReason] = invokePayload.Reason,
            [RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId
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
            IncidentMetadata: new Dictionary<string, string>(),
            ValueSnapshots: valueSnapshots);
    }

    private async ValueTask EnqueueBookmarkCreationWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IReadOnlyCollection<ActivityBookmarkRequest> bookmarkRequests,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
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
                metadata: request.Metadata,
                valueSnapshots: index == 0 ? valueSnapshots : []);

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

    private async ValueTask CommitChildSchedulingActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState state,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{invokeWorkItem.WorkItemId}:activity-inspection-captured:{invokePayload.ActivityExecutionId}";
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = invokeWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = invokeWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = "ChildActivityScheduling",
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = invokePayload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = invokePayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = invokePayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = invokePayload.PinnedExecutable.ArtifactHash
        };
        var inspection = await inspectionAccumulator.BuildProjectionAsync(
            state,
            checkpointId,
            occurredAt,
            valueSnapshots: valueSnapshots,
            metadata: metadata,
            cancellationToken: cancellationToken);
        var childWorkItems = NewChildActivityScheduleWorkItems(idGenerator, invokeWorkItem, invokePayload, scheduleRequests).ToArray();
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{invokeWorkItem.WorkItemId}:activity-inspection-captured:{invokePayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityInspectionCaptured,
                WorkflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [invokePayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: invokePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: metadata)
                ]),
            PostCommitIntents: childWorkItems
                .Select(workItem => NewEnqueueSchedulerWorkIntent(invokeWorkItem, invokePayload.ActivityExecutionId, workItem, occurredAt))
                .ToArray(),
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private async ValueTask EnqueueChildActivityScheduleWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
        CancellationToken cancellationToken)
    {
        foreach (var workItem in NewChildActivityScheduleWorkItems(idGenerator, invokeWorkItem, invokePayload, scheduleRequests))
            await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private IEnumerable<RuntimeSchedulerWorkItem> NewChildActivityScheduleWorkItems(
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests)
    {
        var requests = scheduleRequests.ToArray();
        for (var index = 0; index < requests.Length; index++)
        {
            var request = requests[index];
            var now = _timeProvider.GetUtcNow();
            var childActivityExecutionId = idGenerator.NewActivityExecutionId();
            var payload = new RuntimeScheduleActivityCommandPayload(
                invokePayload.PinnedExecutable,
                request.ExecutableNodeId,
                childActivityExecutionId,
                RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                request.SchedulingActivityExecutionId ?? invokePayload.ActivityExecutionId,
                invokePayload.ActivityExecutionId,
                request.SchedulingProvenance == ActivitySchedulingProvenance.Empty
                    ? ActivitySchedulingProvenance.From(
                        invokeWorkItem.WorkflowExecutionId,
                        invokePayload.ActivityExecutionId,
                        request.SchedulingActivityExecutionId ?? invokePayload.ActivityExecutionId,
                        branchId: null,
                        iterationId: null,
                        executionPathId: null,
                        executionScopeId: null,
                        schedulingCause: RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                        metadata: request.Metadata)
                    : request.SchedulingProvenance);

            var commandMetadata = invokeWorkItem.CommandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            foreach (var item in request.Metadata)
                commandMetadata[item.Key] = item.Value;

            commandMetadata[RuntimeMetadataKeys.ParentActivityExecutionId] = invokePayload.ActivityExecutionId;
            commandMetadata[RuntimeMetadataKeys.ChildExecutableNodeId] = request.ExecutableNodeId;

            var workItem = new RuntimeSchedulerWorkItem(
                workItemId: $"{invokeWorkItem.WorkItemId}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                workflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                commandId: $"{invokeWorkItem.CommandId}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
                envelopeId: invokeWorkItem.EnvelopeId,
                idempotencyKey: $"{invokeWorkItem.IdempotencyKey}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                enqueuedAt: now,
                recordedAt: now,
                sequence: invokeWorkItem.Sequence is { } sequence ? sequence + index + 1 : null,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: commandMetadata,
                envelopeMetadata: invokeWorkItem.EnvelopeMetadata);

            yield return workItem;
        }
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string> commandMetadata,
        ActivityBookmarkRequest request)
    {
        var metadata = commandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in request.Metadata)
            metadata[item.Key] = item.Value;

        metadata[RuntimeMetadataKeys.BookmarkId] = request.BookmarkId;
        metadata[RuntimeMetadataKeys.ResumeTargetId] = request.ResumeTargetId;
        metadata[RuntimeMetadataKeys.StimulusType] = request.StimulusType;
        metadata[RuntimeMetadataKeys.StimulusHash] = request.StimulusHash;
        return metadata;
    }

    private async ValueTask EnqueueCompletionWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState completedState,
        CancellationToken cancellationToken)
    {
        var workItem = NewCompletionWorkItem(invokeWorkItem, invokePayload, completedState);
        await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private async ValueTask CommitCompletedActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState completedState,
        IReadOnlyCollection<string> outcomeNames,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{invokeWorkItem.WorkItemId}:activity-completed:{invokePayload.ActivityExecutionId}";
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = invokeWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = invokeWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = invokePayload.Reason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = invokePayload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = invokePayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = invokePayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = invokePayload.PinnedExecutable.ArtifactHash
        };
        var inspection = await inspectionAccumulator.BuildProjectionAsync(
            completedState,
            checkpointId,
            occurredAt,
            outcomeNames: outcomeNames,
            valueSnapshots: valueSnapshots,
            metadata: metadata,
            cancellationToken: cancellationToken);
        var completionWorkItem = NewCompletionWorkItem(invokeWorkItem, invokePayload, completedState);
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{invokeWorkItem.WorkItemId}:activity-completed:{invokePayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityCompleted,
                WorkflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [invokePayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: invokePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: completedState,
                        Metadata: metadata)
                ],
                bookmarks: [],
                durableValues: durableValueChanges,
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: invokePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: metadata)
                ]),
            PostCommitIntents: [NewEnqueueSchedulerWorkIntent(invokeWorkItem, invokePayload.ActivityExecutionId, completionWorkItem, occurredAt)],
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private RuntimeSchedulerWorkItem NewCompletionWorkItem(
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState completedState)
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

        return new RuntimeSchedulerWorkItem(
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
    }

    private static RuntimePostCommitIntent NewEnqueueSchedulerWorkIntent(
        RuntimeSchedulerWorkItem sourceWorkItem,
        string activityExecutionId,
        RuntimeSchedulerWorkItem schedulerWorkItem,
        DateTimeOffset recordedAt) =>
        new(
            intentId: $"{sourceWorkItem.WorkItemId}:post-commit:{schedulerWorkItem.WorkItemId}",
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: recordedAt,
            activityExecutionId: activityExecutionId,
            idempotencyKey: $"{sourceWorkItem.IdempotencyKey}:post-commit:{schedulerWorkItem.IdempotencyKey}",
            payload: JsonSerializer.SerializeToElement(schedulerWorkItem),
            metadata: sourceWorkItem.CommandMetadata);

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
        metadata[RuntimeMetadataKeys.InvokeReason] = invokePayload.Reason;
        metadata[RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId;
        metadata[RuntimeMetadataKeys.CompletionOutcomeNames] = JsonSerializer.Serialize(outcomeNames);

        if (skipped)
            metadata[RuntimeMetadataKeys.InvokeSkipped] = bool.TrueString;

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
        if (completedState.Metadata.TryGetValue(RuntimeMetadataKeys.CompletionOutcomeNames, out var serializedOutcomeNames))
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
