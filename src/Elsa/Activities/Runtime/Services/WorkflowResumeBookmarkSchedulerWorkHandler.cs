using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

public sealed class WorkflowResumeBookmarkSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowResumeBookmarkSchedulerWorkHandler);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeProvider _timeProvider;

    public WorkflowResumeBookmarkSchedulerWorkHandler(
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _serviceScopeFactory = serviceScopeFactory;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.ResumeBookmark;
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var resumePayload = DeserializeResumePayload(workItem);
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var workflowExecutableStore = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var activityExecutionStateStore = scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>();
        var bookmarkStateStore = scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>();
        var bookmarkConsumptionCheckpointService = scope.ServiceProvider.GetRequiredService<IBookmarkConsumptionCheckpointService>();
        var schedulerWorkQueue = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var checkpointCommitter = scope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>();
        var activityFaultIncidentRecorder = scope.ServiceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        var durableValueStateStore = scope.ServiceProvider.GetRequiredService<IDurableValueStateStore>();
        var payloadCapturePolicy = scope.ServiceProvider.GetService<IRuntimePayloadCapturePolicy>() ?? new DefaultRuntimePayloadCapturePolicy();

        var executable = await workflowExecutableStore.FindAsync(resumePayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(resumePayload.PinnedExecutable.ArtifactId);

        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(workItem, resumePayload.PinnedExecutable, executable.Identity);

        var executableNode = SchedulerWorkHandlerHelpers.ResolveExecutableNode(workItem, executable, resumePayload.ExecutableNodeId, "ResumeBookmark");
        if (executableNode.ActivityContract is null)
            throw new InvalidOperationException($"VF-ACT-001: Executable CLR activity node '{executableNode.ExecutableNodeId}' has no pinned activity contract.");

        if (!executable.ResumeTargets.TryGetValue(resumePayload.ResumeTargetId, out var resumeTarget))
            throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' references resume target '{resumePayload.ResumeTargetId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        if (!StringComparer.Ordinal.Equals(resumeTarget.ExecutableNodeId, resumePayload.ExecutableNodeId))
            throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' references executable node '{resumePayload.ExecutableNodeId}', but resume target '{resumePayload.ResumeTargetId}' points at executable node '{resumeTarget.ExecutableNodeId}'.");

        var state = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, resumePayload.ActivityExecutionId, cancellationToken);
        if (state is null)
            throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' references missing activity execution '{resumePayload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, resumePayload.ExecutableNodeId))
            throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' references executable node '{resumePayload.ExecutableNodeId}', but activity execution '{resumePayload.ActivityExecutionId}' belongs to executable node '{state.Execution.ExecutableNodeId}'.");

        var bookmark = await bookmarkStateStore.FindAsync(workItem.WorkflowExecutionId, resumePayload.BookmarkId, cancellationToken);

        if (state.Status == ActivityExecutionStatus.Completed)
        {
            if (bookmark is not null)
            {
                ValidateBookmarkMatchesPayload(workItem, resumePayload, bookmark);
                await bookmarkConsumptionCheckpointService.CommitAsync(new BookmarkConsumptionCheckpointRequest(workItem, resumePayload, bookmark, state, NewCompletionWorkItem(workItem, resumePayload, state)), cancellationToken);
            }

            return;
        }

        state.EnsureValueFlowCompatible();

        if (state.Status is ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Recovered)
            return;

        if (bookmark is null)
            return;

        ValidateBookmarkMatchesPayload(workItem, resumePayload, bookmark);

        if (!TryResolveTypedTriggerDelivery(state, bookmark, resumePayload, workItem, out var triggerDelivery))
            return;

        await ResumeActivityAsync(scope.ServiceProvider, checkpointCommitter, activityFaultIncidentRecorder, bookmarkConsumptionCheckpointService, schedulerWorkQueue, durableValueStateStore, payloadCapturePolicy, workItem, resumePayload, bookmark, executable, executableNode, state, triggerDelivery!, cancellationToken);
    }

    private async ValueTask ResumeActivityAsync(
        IServiceProvider serviceProvider,
        RuntimeCheckpointCommitter checkpointCommitter,
        ActivityFaultIncidentRecorder activityFaultIncidentRecorder,
        IBookmarkConsumptionCheckpointService bookmarkConsumptionCheckpointService,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IDurableValueStateStore durableValueStateStore,
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        BookmarkState bookmark,
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        ActivityTriggerDelivery triggerDelivery,
        CancellationToken cancellationToken)
    {
        ActivityExecutionState executionState = state;
        ActivityAttempt resumeAttempt;
        try
        {
            var snapshot = RequireCommittedSnapshot(state, executableNode.ActivityContract!);
            var activationClaim = await ActivityAttemptActivationClaimer.ClaimTypedResumeAsync(
                checkpointCommitter,
                _timeProvider,
                workItem,
                resumePayload,
                state,
                triggerDelivery,
                cancellationToken);
            resumeAttempt = activationClaim.Attempt;
            executionState = activationClaim.State with { InputSnapshot = snapshot };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordFaultAsync(serviceProvider, activityFaultIncidentRecorder, checkpointCommitter, workItem, resumePayload, state, exception, "InputMaterializationFailed", [], cancellationToken);
            return;
        }
        var valueSnapshots = new List<ActivityExecutionInspectionValueSnapshot>();
        IActivity activity;
        SimpleActivityExecutionContext context;
        ActivityActivationLease? activationLease = null;
        try
        {
            var contract = executableNode.ActivityContract!;
            valueSnapshots.AddRange(ActivityExecutionInspection.BuildInputValueSnapshots(
                payloadCapturePolicy,
                workItem,
                resumePayload.ActivityExecutionId,
                resumePayload.ExecutableNodeId,
                contract,
                executionState.InputSnapshot!,
                RuntimeMetadataKeys.ResumeSchedulerWorkItemId,
                _timeProvider.GetUtcNow()));

            // Transient activation and one-time snapshot hydration run
            // inside a fault boundary on the resume path too (#325, sibling of #317). Previously this step sat
            // between the input-materialization try/catch and the resume-execution try/catch, so a binder/constructor
            // throw escaped to the scheduler loop and left the run silently at Running with no incident. Recording it
            // as a blocking incident faults the activity and surfaces a queryable cause, distinct from
            // InputMaterializationFailed and the ActivityResumeFaulted resume-method failure below.
            activationLease = await serviceProvider.GetRequiredService<IActivityActivator>().ActivateAsync(
                new ActivityActivationRequest(contract, executionState.InputSnapshot!, resumeAttempt, state.PrivateState, triggerDelivery),
                cancellationToken);
            activity = activationLease.Activity;

            context = SimpleActivityExecutionContext.ForExecution(
                activity,
                cancellationToken,
                workItem.WorkflowExecutionId,
                resumePayload.PinnedExecutable,
                workItem,
                executableNode,
                executionState,
                variableScope: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (activationLease is not null)
                await activationLease.DisposeAsync();
            await RecordFaultAsync(serviceProvider, activityFaultIncidentRecorder, checkpointCommitter, workItem, resumePayload, executionState, exception, "ActivityResumeConstructionFailed", valueSnapshots, cancellationToken);
            return;
        }

        ActivityCompletionProjection? typedCompletion = null;
        ActivityExecutionState? replacementSuspendedState = null;
        try
        {
            var contract = executableNode.ActivityContract!;
            if (activity is not IStatefulActivity statefulActivity)
                throw new InvalidOperationException($"Typed activity '{activity.GetType().FullName}' does not implement the stateful resume contract.");

            var transition = await statefulActivity.ResumeAsync(NewTypedResumeRequest(
                workItem,
                resumePayload,
                executionState,
                resumeAttempt,
                triggerDelivery,
                cancellationToken));
            if (transition is IStatefulActivitySuspensionTransition suspension)
            {
                ValidateStatefulSuspensionRegistrations(executable, executableNode, suspension);
                replacementSuspendedState = StatefulActivitySuspensionProjector.Project(
                    executionState,
                    resumeAttempt,
                    suspension,
                    _timeProvider.GetUtcNow()) with
                {
                    TriggerDeliveries = MarkTriggerConsumed(executionState.TriggerDeliveries!, triggerDelivery.DeliveryId),
                    BookmarkIds = RemoveBookmark(executionState.BookmarkIds, bookmark.BookmarkId)
                };
            }
            else if (transition is IActivityCompletionTransition)
            {
                typedCompletion = await serviceProvider.GetRequiredService<ActivityCompletionProjector>().ProjectAsync(
                    workItem.WorkflowExecutionId,
                    state.InvocationId,
                    resumeAttempt,
                    contract,
                    transition,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            }
            else if (transition is IActivityFaultTransition faultTransition)
            {
                var fault = faultTransition.Fault;
                executionState = executionState with
                {
                    Fault = new NormalizedActivityFault(
                        fault.Code,
                        typeof(ActivityFault).FullName!,
                        fault.Message,
                        sanitizedStackTrace: null,
                        fault.IsRetryable)
                };
                await RecordFaultAsync(
                    serviceProvider,
                    activityFaultIncidentRecorder,
                    checkpointCommitter,
                    workItem,
                    resumePayload,
                    executionState,
                    new ActivityTransitionFaultException(fault),
                    "ActivityReturnedFault",
                    valueSnapshots,
                    cancellationToken);
                return;
            }
            else if (transition is IActivityCancellationTransition cancellationTransition)
            {
                executionState = executionState with
                {
                    TriggerDeliveries = MarkTriggerConsumed(executionState.TriggerDeliveries!, triggerDelivery.DeliveryId),
                    BookmarkIds = RemoveBookmark(executionState.BookmarkIds, bookmark.BookmarkId)
                };
                await ActivityCancellationCheckpointService.CommitAsync(
                    checkpointCommitter,
                    serviceProvider.GetService<IRuntimeActivityExecutionInspectionAccumulator>(),
                    _timeProvider,
                    workItem,
                    executionState,
                    cancellationTransition.Reason,
                    valueSnapshots,
                    consumedBookmark: bookmark,
                    cancellationToken: cancellationToken);
                return;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Stateful resume transition '{transition.Kind}' is not yet supported by the resume checkpoint path.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordFaultAsync(serviceProvider, activityFaultIncidentRecorder, checkpointCommitter, workItem, resumePayload, executionState, exception, "ActivityResumeFaulted", valueSnapshots, cancellationToken);
            return;
        }
        finally
        {
            if (activationLease is not null)
                await activationLease.DisposeAsync();
        }

        var recordedOutputs = typedCompletion is null
            ? []
            : typedCompletion.Projections
                .Where(item => item.Value.Presence != ValuePresence.Absent && item.Value.Policy.Storage != DurableValueStorage.External)
                .Select(item => new RecordedActivityOutput(
                    item.Key,
                    item.Value.Presence == ValuePresence.ExplicitNull ? null : item.Value.InlineValue))
                .ToArray();
        valueSnapshots.AddRange(ActivityExecutionInspection.BuildOutputValueSnapshots(
            payloadCapturePolicy,
            workItem,
            resumePayload.ActivityExecutionId,
            resumePayload.ExecutableNodeId,
            executableNode.ActivityContract,
            recordedOutputs,
            RuntimeMetadataKeys.ResumeSchedulerWorkItemId,
            _timeProvider.GetUtcNow()));

        if (replacementSuspendedState is not null)
        {
            var replacementWorkItems = NewReplacementBookmarkWorkItems(workItem, resumePayload, replacementSuspendedState).ToArray();
            await bookmarkConsumptionCheckpointService.CommitAsync(
                new BookmarkConsumptionCheckpointRequest(
                    workItem,
                    resumePayload,
                    bookmark,
                    replacementSuspendedState,
                    valueSnapshots: valueSnapshots,
                    durableValueChanges: [],
                    continuationWorkItems: replacementWorkItems),
                cancellationToken);
            return;
        }

        IReadOnlyCollection<string> outcomeNames = typedCompletion is null
            ? [ActivityOutcomes.Done]
            : [typedCompletion.Completion.OutcomeKey];
        var completedState = CompleteActivity(workItem, resumePayload, executionState, outcomeNames);
        if (typedCompletion is not null)
        {
            completedState = completedState with
            {
                Completion = typedCompletion.Completion,
                PrivateState = null,
                TriggerRegistrations = [],
                TriggerDeliveries = MarkTriggerConsumed(completedState.TriggerDeliveries!, triggerDelivery.DeliveryId),
                BookmarkIds = RemoveBookmark(completedState.BookmarkIds, bookmark.BookmarkId)
            };
        }
        await bookmarkConsumptionCheckpointService.CommitAsync(new BookmarkConsumptionCheckpointRequest(workItem, resumePayload, bookmark, completedState, NewCompletionWorkItem(workItem, resumePayload, completedState), valueSnapshots, []), cancellationToken);
    }

    private static bool TryResolveTypedTriggerDelivery(
        ActivityExecutionState state,
        BookmarkState bookmark,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        RuntimeSchedulerWorkItem workItem,
        out ActivityTriggerDelivery? delivery)
    {
        delivery = null;
        if (state.Status != ActivityExecutionStatus.Suspended ||
            state.PrivateState is null ||
            resumePayload.TriggerDelivery is not { } metadata)
            return false;

        var registrations = state.TriggerRegistrations?
            .Where(candidate => StringComparer.Ordinal.Equals(candidate.RegistrationId, bookmark.BookmarkId))
            .ToArray() ?? [];
        if (registrations.Length != 1)
            return false;

        var registration = registrations[0];
        if (!StringComparer.Ordinal.Equals(registration.InvocationId, state.InvocationId) ||
            !StringComparer.Ordinal.Equals(registration.ResumeTargetKey, bookmark.ResumeTargetId) ||
            !StringComparer.Ordinal.Equals(registration.StimulusType, bookmark.StimulusType) ||
            !StringComparer.Ordinal.Equals(registration.StimulusHash, bookmark.StimulusHash) ||
            !SameType(registration.PayloadType, metadata.PayloadType))
            return false;

        var priorDeliveries = state.TriggerDeliveries ?? [];
        var duplicate = priorDeliveries.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.RegistrationId, registration.RegistrationId) &&
            candidate.Status is ActivityTriggerDeliveryStatus.Received or ActivityTriggerDeliveryStatus.Consumed &&
            (StringComparer.Ordinal.Equals(candidate.DeliveryId, metadata.DeliveryId) ||
             registration.DeduplicationPolicy == ActivityTriggerDeduplicationPolicy.Once ||
             StringComparer.Ordinal.Equals(candidate.DeduplicationKey, metadata.DeduplicationKey)));
        if (duplicate is not null)
        {
            var isClaimRedelivery = duplicate.Status == ActivityTriggerDeliveryStatus.Received &&
                StringComparer.Ordinal.Equals(duplicate.DeliveryId, metadata.DeliveryId) &&
                state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId, out var claimWorkItemId) &&
                StringComparer.Ordinal.Equals(claimWorkItemId, workItem.WorkItemId) &&
                state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var claimedAttemptId) &&
                state.Attempts?.Any(attempt => attempt.EndedAt is null && StringComparer.Ordinal.Equals(attempt.AttemptId, claimedAttemptId)) == true;
            if (isClaimRedelivery)
            {
                delivery = duplicate;
                return true;
            }

            return false;
        }

        var payload = resumePayload.Input is { } input && input.ValueKind is not JsonValueKind.Null
            ? ValueEnvelope.Inline(registration.PayloadType, input, ValueProtectionPolicy.InstanceInline)
            : ValueEnvelope.Null(registration.PayloadType, ValueProtectionPolicy.InstanceInline);
        delivery = new ActivityTriggerDelivery(
            metadata.DeliveryId,
            registration.RegistrationId,
            metadata.PayloadType,
            payload,
            metadata.ProviderId,
            metadata.ReceivedAt,
            metadata.DeduplicationKey,
            ActivityTriggerDeliveryStatus.Received);
        return true;
    }

    private static void ValidateStatefulSuspensionRegistrations(
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        IStatefulActivitySuspensionTransition suspension)
    {
        foreach (var registration in suspension.Registrations)
        {
            if (!executable.ResumeTargets.TryGetValue(registration.ResumeTargetKey, out var resumeTarget))
            {
                throw new InvalidOperationException(
                    $"Stateful activity '{executableNode.ExecutableNodeId}' registered missing resume target '{registration.ResumeTargetKey}'.");
            }

            if (!StringComparer.Ordinal.Equals(resumeTarget.ExecutableNodeId, executableNode.ExecutableNodeId))
            {
                throw new InvalidOperationException(
                    $"Resume target '{registration.ResumeTargetKey}' belongs to executable node '{resumeTarget.ExecutableNodeId}', not '{executableNode.ExecutableNodeId}'.");
            }
        }
    }

    private IEnumerable<RuntimeSchedulerWorkItem> NewReplacementBookmarkWorkItems(
        RuntimeSchedulerWorkItem resumeWorkItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState suspendedState)
    {
        var registrations = suspendedState.TriggerRegistrations?.ToArray() ?? [];
        for (var index = 0; index < registrations.Length; index++)
        {
            var registration = registrations[index];
            var now = _timeProvider.GetUtcNow();
            var payload = new RuntimeCreateBookmarkCommandPayload(
                pinnedExecutable: resumePayload.PinnedExecutable,
                bookmarkId: registration.RegistrationId,
                activityExecutionId: resumePayload.ActivityExecutionId,
                executableNodeId: resumePayload.ExecutableNodeId,
                resumeTargetId: registration.ResumeTargetKey,
                stimulusType: registration.StimulusType,
                stimulusHash: registration.StimulusHash,
                payload: null,
                expiresAt: null,
                reason: RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason,
                metadata: registration.Metadata,
                valueSnapshots: [],
                durableValueChanges: []);

            yield return new RuntimeSchedulerWorkItem(
                workItemId: $"{resumeWorkItem.WorkItemId}:create-bookmark:{registration.RegistrationId}",
                workflowExecutionId: resumeWorkItem.WorkflowExecutionId,
                commandId: $"{resumeWorkItem.CommandId}:create-bookmark:{registration.RegistrationId}",
                commandKind: WorkflowExecutionCommandKind.CreateBookmark,
                envelopeId: resumeWorkItem.EnvelopeId,
                idempotencyKey: $"{resumeWorkItem.IdempotencyKey}:create-bookmark:{registration.RegistrationId}",
                enqueuedAt: now,
                recordedAt: now,
                sequence: resumeWorkItem.Sequence is { } sequence ? sequence + index + 1 : null,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: resumeWorkItem.CommandMetadata,
                envelopeMetadata: resumeWorkItem.EnvelopeMetadata);
        }
    }

    private static ActivityResumeRequest NewTypedResumeRequest(
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState state,
        ActivityAttempt attempt,
        ActivityTriggerDelivery trigger,
        CancellationToken cancellationToken)
    {
        var privateState = state.PrivateState
            ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' has no committed private state.");
        if (!StringComparer.Ordinal.Equals(privateState.InvocationId, state.InvocationId))
            throw new InvalidOperationException($"Committed private state does not belong to activity invocation '{state.InvocationId}'.");

        return new ActivityResumeRequest(
            new ActivityExecutionContext(
                workItem.WorkflowExecutionId,
                state.InvocationId,
                attempt.AttemptId,
                resumePayload.ExecutableNodeId,
                cancellationToken),
            privateState.Value.Type,
            RequireInlinePayload(privateState.Value, "private state"),
            trigger.PayloadType,
            RequireInlinePayload(trigger.Payload, "trigger"),
            trigger.RegistrationId,
            trigger.DeliveryId);
    }

    private static JsonElement RequireInlinePayload(ValueEnvelope envelope, string role) => envelope.Presence switch
    {
        ValuePresence.Present when envelope.InlineValue is { } inline => inline,
        ValuePresence.ExplicitNull => JsonSerializer.SerializeToElement<object?>(null),
        _ => throw new InvalidOperationException($"Typed activity {role} must carry an inline persistable payload.")
    };

    private static IReadOnlyCollection<ActivityTriggerDelivery> MarkTriggerConsumed(
        IReadOnlyCollection<ActivityTriggerDelivery> deliveries,
        string deliveryId) =>
        deliveries
            .Select(delivery => !StringComparer.Ordinal.Equals(delivery.DeliveryId, deliveryId)
                ? delivery
                : new ActivityTriggerDelivery(
                    delivery.DeliveryId,
                    delivery.RegistrationId,
                    delivery.PayloadType,
                    delivery.Payload,
                    delivery.ProviderId,
                    delivery.ReceivedAt,
                    delivery.DeduplicationKey,
                    ActivityTriggerDeliveryStatus.Consumed))
            .ToArray();

    private static IReadOnlyCollection<string> RemoveBookmark(
        IReadOnlyCollection<string> bookmarkIds,
        string bookmarkId) =>
        bookmarkIds.Where(candidate => !StringComparer.Ordinal.Equals(candidate, bookmarkId)).ToArray();

    private static bool SameType(Elsa.Primitives.Models.ValueTypeDescriptor left, Elsa.Primitives.Models.ValueTypeDescriptor right) =>
        StringComparer.Ordinal.Equals(left.Alias, right.Alias) &&
        left.CollectionKind == right.CollectionKind &&
        left.SchemaVersion == right.SchemaVersion &&
        StringComparer.Ordinal.Equals(left.Schema?.GetRawText(), right.Schema?.GetRawText());

    private RuntimeSchedulerWorkItem NewCompletionWorkItem(
        RuntimeSchedulerWorkItem resumeWorkItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState completedState)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeCompleteActivityCommandPayload(
            resumePayload.PinnedExecutable,
            resumePayload.ExecutableNodeId,
            resumePayload.ActivityExecutionId,
            completedState.ParentActivityExecutionId,
            completedState.BranchId,
            ReadCompletionOutcomeNames(completedState),
            RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason);

        return new RuntimeSchedulerWorkItem(
            workItemId: $"{resumeWorkItem.WorkItemId}:complete:{resumePayload.ActivityExecutionId}",
            workflowExecutionId: resumeWorkItem.WorkflowExecutionId,
            commandId: $"{resumeWorkItem.CommandId}:complete:{resumePayload.ActivityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: resumeWorkItem.EnvelopeId,
            idempotencyKey: $"{resumeWorkItem.IdempotencyKey}:complete:{resumePayload.ActivityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: resumeWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: resumeWorkItem.CommandMetadata,
            envelopeMetadata: resumeWorkItem.EnvelopeMetadata);
    }

    private static RuntimeResumeBookmarkCommandPayload DeserializeResumePayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkHandlerHelpers.DeserializePayload(
            workItem,
            requiresPayloadMessage: "ResumeBookmark scheduler work item requires a resume bookmark payload.",
            resolvedToNullMessage: "ResumeBookmark scheduler work item payload resolved to null.",
            invalidPayloadMessage: "ResumeBookmark scheduler work item payload is not a valid resume bookmark payload.",
            deserialize: static (_, payload) => payload.Deserialize<RuntimeResumeBookmarkCommandPayload>(),
            isPayloadValidationException: static exception =>
                exception is JsonException or NotSupportedException ||
                exception is ArgumentException argumentException && IsResumePayloadValidationException(argumentException));

    private static bool IsResumePayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "bookmarkId" or
            "activityExecutionId" or
            "executableNodeId" or
            "resumeTargetId" or
            "stimulusType" or
            "stimulusHash" or
            "reason";

    private static void ValidateBookmarkMatchesPayload(
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        BookmarkState bookmark)
    {
        if (!StringComparer.Ordinal.Equals(bookmark.ActivityExecutionId, resumePayload.ActivityExecutionId))
            throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' references activity execution '{resumePayload.ActivityExecutionId}', but bookmark '{bookmark.BookmarkId}' belongs to activity execution '{bookmark.ActivityExecutionId}'.");

        if (!StringComparer.Ordinal.Equals(bookmark.ExecutableNodeId, resumePayload.ExecutableNodeId))
            throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' references executable node '{resumePayload.ExecutableNodeId}', but bookmark '{bookmark.BookmarkId}' belongs to executable node '{bookmark.ExecutableNodeId}'.");

        if (!StringComparer.Ordinal.Equals(bookmark.ResumeTargetId, resumePayload.ResumeTargetId))
            throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' references resume target '{resumePayload.ResumeTargetId}', but bookmark '{bookmark.BookmarkId}' points at resume target '{bookmark.ResumeTargetId}'.");

        if (!StringComparer.Ordinal.Equals(bookmark.StimulusType, resumePayload.StimulusType))
            throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' references stimulus type '{resumePayload.StimulusType}', but bookmark '{bookmark.BookmarkId}' expects stimulus type '{bookmark.StimulusType}'.");

        if (!StringComparer.Ordinal.Equals(bookmark.StimulusHash, resumePayload.StimulusHash))
            throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' references stimulus hash '{resumePayload.StimulusHash}', but bookmark '{bookmark.BookmarkId}' expects stimulus hash '{bookmark.StimulusHash}'.");
    }

    private ActivityExecutionState CompleteActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState state,
        IReadOnlyCollection<string> outcomeNames)
    {
        var completedAt = _timeProvider.GetUtcNow();
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ResumeReason] = resumePayload.Reason;
        metadata[RuntimeMetadataKeys.ResumeSchedulerWorkItemId] = workItem.WorkItemId;
        metadata[RuntimeMetadataKeys.BookmarkId] = resumePayload.BookmarkId;
        metadata[RuntimeMetadataKeys.ResumeTargetId] = resumePayload.ResumeTargetId;
        metadata[RuntimeMetadataKeys.CompletionOutcomeNames] = JsonSerializer.Serialize(outcomeNames);

        return RuntimeContainerScopeService.CloseOwnedFrames(state with
        {
            Status = ActivityExecutionStatus.Completed,
            SubStatus = null,
            CompletedAt = completedAt,
            Attempts = EndOpenAttempt(state, Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Complete, completedAt).Attempts,
            Metadata = metadata
        });
    }

    private static ActivityInputSnapshot RequireCommittedSnapshot(ActivityExecutionState state, ActivityContract contract)
    {
        var snapshot = state.InputSnapshot
            ?? throw new InvalidOperationException($"VF-ACT-009: Typed activity invocation '{state.InvocationId}' has no committed input snapshot.");

        if (!StringComparer.Ordinal.Equals(snapshot.InvocationId, state.InvocationId) ||
            !StringComparer.Ordinal.Equals(snapshot.ContractFingerprint, contract.SchemaFingerprint))
            throw new InvalidOperationException($"VF-ACT-001: Typed activity invocation '{state.InvocationId}' does not match its pinned input snapshot contract.");

        if (state.ContractIdentity is not { } identity ||
            !StringComparer.Ordinal.Equals(identity.ActivityTypeKey, contract.ActivityTypeKey) ||
            !StringComparer.Ordinal.Equals(identity.ContractVersion, contract.ContractVersion) ||
            !StringComparer.Ordinal.Equals(identity.SchemaFingerprint, contract.SchemaFingerprint))
            throw new InvalidOperationException($"VF-ACT-001: Typed activity invocation '{state.InvocationId}' does not match its pinned activity contract.");

        return snapshot;
    }

    private static ActivityExecutionState EndOpenAttempt(
        ActivityExecutionState state,
        Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind transition,
        DateTimeOffset endedAt,
        string? incidentId = null)
    {
        var attempts = state.Attempts?.ToArray() ?? [];
        var openAttempt = attempts.LastOrDefault(attempt => attempt.EndedAt is null);
        if (openAttempt is null)
            return state;

        var endedAttempt = new ActivityAttempt(
            openAttempt.AttemptId,
            openAttempt.InvocationId,
            openAttempt.Ordinal,
            openAttempt.Reason,
            openAttempt.StartedAt,
            endedAt,
            openAttempt.TriggerDeliveryId,
            transition,
            incidentId);
        return state with
        {
            Attempts = attempts
                .Where(attempt => attempt.AttemptId != openAttempt.AttemptId)
                .Append(endedAttempt)
                .OrderBy(attempt => attempt.Ordinal)
                .ToArray()
        };
    }

    private static IReadOnlyCollection<string> ReadCompletionOutcomeNames(ActivityExecutionState completedState)
    {
        if (completedState.Metadata.TryGetValue(RuntimeMetadataKeys.CompletionOutcomeNames, out var serializedOutcomeNames))
        {
            var outcomeNames = JsonSerializer.Deserialize<string[]>(serializedOutcomeNames)
                ?? throw new InvalidOperationException("Persisted completion outcome names resolved to null.");

            return SchedulerWorkHandlerHelpers.NormalizeOutcomeNames(outcomeNames, defaultToDone: false);
        }

        return [ActivityOutcomes.Done];
    }

    // Records a blocking fault incident for the resumed activity and commits it. Each fault arm in
    // ResumeActivityAsync (input materialization, construction/binding, resume-method execution) differs only in
    // its reason and snapshot set; centralizing the request shape + commit here keeps those arms to one call.
    // Like the invoke path, it rides a child-fault parent-evaluation work item along when the faulted activity has
    // a parent fork/join, so a branch that suspends then faults on resume still resolves its parent's join
    // deterministically (#308).
    private async ValueTask RecordFaultAsync(
        IServiceProvider serviceProvider,
        ActivityFaultIncidentRecorder activityFaultIncidentRecorder,
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        CancellationToken cancellationToken)
    {
        var incidentId = ActivityFaultIncidentRecorder.IncidentId(workItem.WorkItemId, resumePayload.ActivityExecutionId, subStatus);
        state = EndOpenAttempt(state, Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, _timeProvider.GetUtcNow(), incidentId);
        var request = NewFaultIncidentRecordRequest(checkpointCommitter, workItem, resumePayload, state, exception, subStatus, valueSnapshots);
        var activityExecutionStateStore = serviceProvider.GetRequiredService<IActivityExecutionStateStore>();
        var parentEvaluation = await ChildFaultParentEvaluation.TryBuildAsync(
            activityExecutionStateStore, _timeProvider, workItem, resumePayload.PinnedExecutable, state, incidentId, cancellationToken);

        await activityFaultIncidentRecorder.CommitAsync(
            parentEvaluation is null ? request : request with { PostCommitSchedulerWorkItemsOrNull = [parentEvaluation] },
            cancellationToken);
    }

    private static ActivityFaultIncidentRecordRequest NewFaultIncidentRecordRequest(
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null)
    {
        var activityMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.ResumeReason] = resumePayload.Reason,
            [RuntimeMetadataKeys.ResumeSchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.BookmarkId] = resumePayload.BookmarkId,
            [RuntimeMetadataKeys.ResumeTargetId] = resumePayload.ResumeTargetId
        };
        var incidentMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.BookmarkId] = resumePayload.BookmarkId,
            [RuntimeMetadataKeys.ResumeTargetId] = resumePayload.ResumeTargetId,
            [RuntimeMetadataKeys.StimulusType] = resumePayload.StimulusType,
            [RuntimeMetadataKeys.StimulusHash] = resumePayload.StimulusHash
        };

        return new ActivityFaultIncidentRecordRequest(
            CheckpointCommitter: checkpointCommitter,
            WorkItem: workItem,
            ActivityExecutionId: resumePayload.ActivityExecutionId,
            ExecutableNodeId: resumePayload.ExecutableNodeId,
            State: state,
            Exception: exception,
            SubStatus: subStatus,
            ActivityMetadata: activityMetadata,
            IncidentMetadata: incidentMetadata,
            ValueSnapshots: valueSnapshots ?? []);
    }

}
