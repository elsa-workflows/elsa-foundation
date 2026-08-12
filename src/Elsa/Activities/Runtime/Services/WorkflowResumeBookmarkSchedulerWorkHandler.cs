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

    /// <summary>
    /// The provider identity recorded for a resume whose stimulus source declared no typed payload contract
    /// (#1014). It marks the delivery as runtime-synthesized from the waiting invocation's own trigger
    /// registration, distinct from an adapter-authored delivery such as <c>Elsa.HttpEndpoint</c>.
    /// </summary>
    public const string UndeclaredStimulusProviderId = "runtime.stimulus";

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
        var activityExecutionStateStore = scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>();
        var bookmarkStateStore = scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>();
        var bookmarkConsumptionCheckpointService = scope.ServiceProvider.GetRequiredService<IBookmarkConsumptionCheckpointService>();
        var schedulerWorkQueue = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var checkpointCommitter = scope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>();
        var activityFaultIncidentRecorder = scope.ServiceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        var durableValueStateStore = scope.ServiceProvider.GetRequiredService<IDurableValueStateStore>();
        var payloadCapturePolicy = scope.ServiceProvider.GetService<IRuntimePayloadCapturePolicy>() ?? new DefaultRuntimePayloadCapturePolicy();

        // spec 111: burst-cached pinned-executable read.
        var executable = await PinnedExecutableRead.FindAsync(scope.ServiceProvider, resumePayload.PinnedExecutable.ArtifactId, cancellationToken);
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
        var deliveryMetadata = ResolveTriggerDeliveryMetadata(workItem, resumePayload, state);

        if (TryResolveClaimedResume(state, workItem, deliveryMetadata, out var claimedDelivery, out var claimedAttempt))
        {
            await ResumeActivityAsync(scope.ServiceProvider, checkpointCommitter, activityFaultIncidentRecorder, bookmarkConsumptionCheckpointService, schedulerWorkQueue, durableValueStateStore, payloadCapturePolicy, workItem, resumePayload, null, [], executable, executableNode, state, claimedDelivery!, claimedAttempt, cancellationToken);
            return;
        }

        if (state.Status == ActivityExecutionStatus.Completed)
        {
            if (bookmark is not null)
            {
                ValidateBookmarkMatchesPayload(workItem, resumePayload, bookmark);
                await bookmarkConsumptionCheckpointService.CommitAsync(
                    BookmarkConsumptionCheckpointRequest.ForStaleBookmarkConsumption(
                        workItem,
                        resumePayload,
                        bookmark,
                        state,
                        NewCompletionWorkItem(workItem, resumePayload, state)),
                    cancellationToken);
            }

            return;
        }

        state.EnsureValueFlowCompatible();

        if (state.Status is ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Recovered)
            return;

        if (bookmark is null)
            return;

        ValidateBookmarkMatchesPayload(workItem, resumePayload, bookmark);

        if (!TryResolveTypedTriggerDelivery(state, bookmark, resumePayload, deliveryMetadata, workItem, out var triggerDelivery))
            return;

        var siblingBookmarks = await LoadOwnedSiblingBookmarksAsync(bookmarkStateStore, workItem, state, bookmark, cancellationToken);
        await ResumeActivityAsync(scope.ServiceProvider, checkpointCommitter, activityFaultIncidentRecorder, bookmarkConsumptionCheckpointService, schedulerWorkQueue, durableValueStateStore, payloadCapturePolicy, workItem, resumePayload, bookmark, siblingBookmarks, executable, executableNode, state, triggerDelivery!, null, cancellationToken);
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
        BookmarkState? bookmark,
        IReadOnlyCollection<BookmarkState> siblingBookmarks,
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        ActivityTriggerDelivery triggerDelivery,
        ActivityAttempt? claimedAttempt,
        CancellationToken cancellationToken)
    {
        ActivityExecutionState executionState = state;
        ActivityAttempt resumeAttempt;
        if (claimedAttempt is null)
        {
            var bookmarkToConsume = bookmark
                ?? throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' cannot claim trigger delivery without bookmark '{resumePayload.BookmarkId}'.");
            var activationClaim = ActivityAttemptActivationClaimer.PrepareTypedResumeClaim(
                _timeProvider,
                workItem,
                resumePayload,
                state,
                triggerDelivery);
            await bookmarkConsumptionCheckpointService.CommitAsync(
                BookmarkConsumptionCheckpointRequest.ForInitialTriggerClaim(
                    workItem,
                    resumePayload,
                    bookmarkToConsume,
                    activationClaim.State,
                    activationClaim.Attempt.AttemptId,
                    siblingBookmarks),
                cancellationToken);
            resumeAttempt = activationClaim.Attempt;
            executionState = activationClaim.State;
        }
        else
        {
            var replacementClaim = ActivityAttemptActivationClaimer.PrepareTypedResumeRedeliveryClaim(
                _timeProvider,
                workItem,
                state,
                triggerDelivery);
            await bookmarkConsumptionCheckpointService.CommitAsync(
                BookmarkConsumptionCheckpointRequest.ForRedeliveryClaim(
                    workItem,
                    resumePayload,
                    replacementClaim.State,
                    replacementClaim.Attempt.AttemptId),
                cancellationToken);
            resumeAttempt = replacementClaim.Attempt;
            executionState = replacementClaim.State;
        }

        try
        {
            var snapshot = RequireCommittedSnapshot(executionState, executableNode.ActivityContract!);
            executionState = executionState with { InputSnapshot = snapshot };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordFaultAsync(serviceProvider, activityFaultIncidentRecorder, checkpointCommitter, workItem, resumePayload, executionState, exception, "InputMaterializationFailed", [], cancellationToken);
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
                new ActivityActivationRequest(contract, executionState.InputSnapshot!, resumeAttempt, state.PrivateState, triggerDelivery, executableNode.Descriptor),
                cancellationToken);
            activity = activationLease.Activity;

            // spec 123 D1: populate the scoped-variable read seam for a marker consumer (the BpmnProcess) from
            // committed frame state, so a bookmark-resume evaluation that reaches a collection-mode loop-start
            // element reads the collection variable through the same committed-basis projection as the other paths.
            var scopeService = new RuntimeContainerScopeService(
                serviceProvider.GetRequiredService<IActivityExecutionStateStore>(),
                serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>());
            var scopedVariableEnvelopes = await scopeService.ProjectScopedVariablesForReaderAsync(
                activity, executable, executionState, cancellationToken);

            context = SimpleActivityExecutionContext.ForExecution(
                activity,
                cancellationToken,
                workItem.WorkflowExecutionId,
                resumePayload.PinnedExecutable,
                workItem,
                executableNode,
                executionState,
                variableScope: null,
                scopedVariableEnvelopes: scopedVariableEnvelopes);
        }
        catch (OperationCanceledException cancellationException) when (cancellationToken.IsCancellationRequested)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            if (disposalException is not null)
                throw new AggregateException("Activity activation cancellation and disposal both failed.", cancellationException, disposalException);
            throw;
        }
        catch (Exception exception)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            var fault = disposalException is null
                ? exception
                : ActivityActivationLeaseDisposer.Combine(exception, disposalException);
            var subStatus = disposalException is null ? "ActivityResumeConstructionFailed" : "ActivityDisposalFailed";
            await RecordFaultAsync(serviceProvider, activityFaultIncidentRecorder, checkpointCommitter, workItem, resumePayload, executionState, fault, subStatus, valueSnapshots, cancellationToken);
            return;
        }

        ActivityCompletionProjection? typedCompletion = null;
        IActivityCompletionTransition? completionTransition = null;
        ActivityExecutionState? replacementSuspendedState = null;
        ActivityFault? returnedFault = null;
        string? returnedCancellationReason = null;
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
                StatefulSuspensionSupport.ValidateRegistrations(executable, executableNode, suspension);
                replacementSuspendedState = StatefulActivitySuspensionProjector.Project(
                    executionState,
                    resumeAttempt,
                    suspension,
                    _timeProvider.GetUtcNow(),
                    key => StatefulSuspensionSupport.ResolveResumeTarget(executable, executableNode, key).ResumeTargetId);
            }
            else if (transition is IActivityCompletionTransition)
            {
                completionTransition = (IActivityCompletionTransition)transition;
                typedCompletion = await serviceProvider.GetRequiredService<ActivityCompletionProjector>().ProjectAsync(
                    workItem.WorkflowExecutionId,
                    executionState.InvocationId,
                    resumeAttempt,
                    contract,
                    transition,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            }
            else if (transition is IActivityFaultTransition faultTransition)
            {
                returnedFault = faultTransition.Fault;
            }
            else if (transition is IActivityCancellationTransition cancellationTransition)
            {
                executionState = executionState with { TriggerRegistrations = [], BookmarkIds = [] };
                returnedCancellationReason = cancellationTransition.Reason;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Stateful resume transition '{transition.Kind}' is not yet supported by the resume checkpoint path.");
            }
        }
        catch (OperationCanceledException cancellationException) when (cancellationToken.IsCancellationRequested)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            if (disposalException is not null)
                throw new AggregateException("Activity resume cancellation and disposal both failed.", cancellationException, disposalException);
            throw;
        }
        catch (Exception exception)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            var fault = disposalException is null
                ? exception
                : ActivityActivationLeaseDisposer.Combine(exception, disposalException);
            var subStatus = disposalException is null ? "ActivityResumeFaulted" : "ActivityDisposalFailed";
            await RecordFaultAsync(serviceProvider, activityFaultIncidentRecorder, checkpointCommitter, workItem, resumePayload, executionState, fault, subStatus, valueSnapshots, cancellationToken);
            return;
        }

        var activationDisposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
        activationLease = null;
        if (activationDisposalException is not null)
        {
            await RecordFaultAsync(
                serviceProvider,
                activityFaultIncidentRecorder,
                checkpointCommitter,
                workItem,
                resumePayload,
                executionState,
                activationDisposalException,
                "ActivityDisposalFailed",
                valueSnapshots,
                cancellationToken);
            return;
        }

        if (returnedFault is not null)
        {
            var faultedState = executionState with
            {
                Fault = returnedFault.ToNormalized()
            };
            await RecordFaultAsync(
                serviceProvider,
                activityFaultIncidentRecorder,
                checkpointCommitter,
                workItem,
                resumePayload,
                faultedState,
                new ActivityTransitionFaultException(returnedFault),
                "ActivityReturnedFault",
                valueSnapshots,
                cancellationToken);
            return;
        }

        if (returnedCancellationReason is not null)
        {
            await ActivityCancellationCheckpointService.CommitAsync(
                checkpointCommitter,
                serviceProvider.GetService<IRuntimeActivityExecutionInspectionAccumulator>(),
                _timeProvider,
                workItem,
                executionState,
                returnedCancellationReason,
                valueSnapshots,
                cancellationToken: cancellationToken);
            return;
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
                BookmarkConsumptionCheckpointRequest.ForSuspension(
                    workItem,
                    resumePayload,
                    replacementSuspendedState,
                    replacementWorkItems,
                    valueSnapshots,
                    []),
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
                TriggerRegistrations = []
            };
        }
        completedState = ActivityAttemptActivationClaimer.CompactTriggerDeliveryHistory(completedState);
        var captureProjection = typedCompletion is null
            ? RuntimeOutputCaptureProjection.Empty
            : await serviceProvider.GetRequiredService<RuntimeOutputCaptureProjector>().ProjectAsync(
                workItem.WorkflowExecutionId,
                executionState.InvocationId,
                executableNode,
                completionTransition!,
                typedCompletion,
                _timeProvider.GetUtcNow(),
                cancellationToken);
        // A workflow-variable output capture writes the canonical root frame in the SAME commit as the
        // completion (#972), mirroring how the Set intrinsic commits its changed frame.
        var workflowVariableWriteBack = await RuntimeWorkflowVariableCaptureWriteBack.BuildStateChangeAsync(
            serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>(),
            workItem.WorkflowExecutionId,
            executableNode.ExecutableNodeId,
            captureProjection.WorkflowVariableWrites,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                [RuntimeMetadataKeys.CheckpointReason] = RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason
            },
            cancellationToken);
        await bookmarkConsumptionCheckpointService.CommitAsync(
            BookmarkConsumptionCheckpointRequest.ForCompletion(
                workItem,
                resumePayload,
                completedState,
                NewCompletionWorkItem(workItem, resumePayload, completedState),
                valueSnapshots,
                captureProjection.DurableValues,
                workflowVariableWriteBack),
            cancellationToken);
    }

    private static async ValueTask<IReadOnlyCollection<BookmarkState>> LoadOwnedSiblingBookmarksAsync(
        IBookmarkStateStore bookmarkStateStore,
        RuntimeSchedulerWorkItem workItem,
        ActivityExecutionState state,
        BookmarkState selectedBookmark,
        CancellationToken cancellationToken)
    {
        var registrations = (state.TriggerRegistrations ?? [])
            .ToDictionary(registration => registration.RegistrationId, StringComparer.Ordinal);
        var siblings = new List<BookmarkState>();
        var siblingIds = state.BookmarkIds
            .Concat(registrations.Keys)
            .Where(id => !StringComparer.Ordinal.Equals(id, selectedBookmark.BookmarkId))
            .Distinct(StringComparer.Ordinal);
        foreach (var bookmarkId in siblingIds)
        {
            if (!registrations.TryGetValue(bookmarkId, out var registration))
                throw new InvalidOperationException($"VF-ACT-008: Activity invocation '{state.InvocationId}' owns bookmark '{bookmarkId}' without a matching trigger registration.");

            var sibling = await bookmarkStateStore.FindAsync(workItem.WorkflowExecutionId, bookmarkId, cancellationToken);
            if (sibling is null)
                continue;
            if (!StringComparer.Ordinal.Equals(sibling.WorkflowExecutionId, workItem.WorkflowExecutionId) ||
                !StringComparer.Ordinal.Equals(sibling.ActivityExecutionId, state.Execution.ActivityExecutionId) ||
                !StringComparer.Ordinal.Equals(sibling.ExecutableNodeId, state.Execution.ExecutableNodeId) ||
                !StringComparer.Ordinal.Equals(sibling.ResumeTargetId, registration.ResumeTargetKey) ||
                !StringComparer.Ordinal.Equals(sibling.StimulusType, registration.StimulusType) ||
                !StringComparer.Ordinal.Equals(sibling.StimulusHash, registration.StimulusHash))
                throw new InvalidOperationException($"VF-ACT-008: Sibling bookmark '{bookmarkId}' does not match its owned trigger registration on activity invocation '{state.InvocationId}'.");

            siblings.Add(sibling);
        }

        return siblings;
    }

    /// <summary>
    /// Resolves the typed delivery identity this resume is carrying (#1014). A stimulus source that declares a
    /// payload contract (the HTTP endpoint, the <c>DispatchWorkflow</c> parent resume) supplies it on the durable
    /// command payload. A source that does not — the runtime stimuli endpoint, the in-workflow <c>PublishEvent</c>
    /// intent, the recurring-trigger pump — would otherwise resolve to no delivery at all, and the resume would be
    /// dropped silently: bookmark intact, run parked forever, no incident. For those, the waiting invocation's own
    /// committed trigger registration is the authority on the payload contract and the delivery adopts it; the
    /// delivery identity is derived from the durable work item, so a redelivery of the same envelope synthesizes
    /// byte-identical metadata and still lands on the claim-redelivery path (where the registration is already
    /// consumed and the recorded delivery carries the contract instead).
    /// </summary>
    private static RuntimeTypedTriggerDeliveryMetadata? ResolveTriggerDeliveryMetadata(
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState state)
    {
        if (resumePayload.TriggerDelivery is { } declared)
            return declared;

        var deliveryId = workItem.CommandId;
        var payloadType = state.TriggerRegistrations?
                .FirstOrDefault(registration => StringComparer.Ordinal.Equals(registration.RegistrationId, resumePayload.BookmarkId))?.PayloadType
            ?? state.TriggerDeliveries?
                .FirstOrDefault(delivery => StringComparer.Ordinal.Equals(delivery.DeliveryId, deliveryId))?.PayloadType;
        if (payloadType is null)
            return null;

        return new RuntimeTypedTriggerDeliveryMetadata(
            deliveryId: deliveryId,
            payloadType: payloadType,
            providerId: UndeclaredStimulusProviderId,
            receivedAt: workItem.EnqueuedAt,
            deduplicationKey: workItem.IdempotencyKey);
    }

    private static bool TryResolveClaimedResume(
        ActivityExecutionState state,
        RuntimeSchedulerWorkItem workItem,
        RuntimeTypedTriggerDeliveryMetadata? deliveryMetadata,
        out ActivityTriggerDelivery? delivery,
        out ActivityAttempt? attempt)
    {
        delivery = null;
        attempt = null;
        if (state.Status != ActivityExecutionStatus.Running ||
            state.PrivateState is null ||
            deliveryMetadata is not { } metadata ||
            !state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId, out var claimedWorkItemId) ||
            !StringComparer.Ordinal.Equals(claimedWorkItemId, workItem.WorkItemId) ||
            !state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var claimedAttemptId))
            return false;

        attempt = state.Attempts?
            .SingleOrDefault(candidate => candidate.EndedAt is null && StringComparer.Ordinal.Equals(candidate.AttemptId, claimedAttemptId));
        if (attempt is null || !StringComparer.Ordinal.Equals(attempt.TriggerDeliveryId, metadata.DeliveryId))
            return false;

        delivery = state.TriggerDeliveries?
            .SingleOrDefault(candidate =>
                candidate.Status == ActivityTriggerDeliveryStatus.Consumed &&
                StringComparer.Ordinal.Equals(candidate.DeliveryId, metadata.DeliveryId) &&
                StringComparer.Ordinal.Equals(candidate.ProviderId, metadata.ProviderId) &&
                StringComparer.Ordinal.Equals(candidate.DeduplicationKey, metadata.DeduplicationKey) &&
                SameType(candidate.PayloadType, metadata.PayloadType));
        return delivery is not null;
    }

    private static bool TryResolveTypedTriggerDelivery(
        ActivityExecutionState state,
        BookmarkState bookmark,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        RuntimeTypedTriggerDeliveryMetadata? deliveryMetadata,
        RuntimeSchedulerWorkItem workItem,
        out ActivityTriggerDelivery? delivery)
    {
        delivery = null;
        if (state.Status != ActivityExecutionStatus.Suspended ||
            state.PrivateState is null ||
            deliveryMetadata is not { } metadata)
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
                workItemId: RuntimeChainId.Derive(resumeWorkItem.WorkItemId, $"create-bookmark:{registration.RegistrationId}"),
                workflowExecutionId: resumeWorkItem.WorkflowExecutionId,
                commandId: RuntimeChainId.Derive(resumeWorkItem.CommandId, $"create-bookmark:{registration.RegistrationId}"),
                commandKind: WorkflowExecutionCommandKind.CreateBookmark,
                envelopeId: resumeWorkItem.EnvelopeId,
                idempotencyKey: RuntimeChainId.Derive(resumeWorkItem.IdempotencyKey, $"create-bookmark:{registration.RegistrationId}"),
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
            SchedulerWorkHandlerHelpers.ReadCompletionOutcomeNames(completedState, skippedSubStatus: null),
            RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason);

        return new RuntimeSchedulerWorkItem(
            workItemId: RuntimeChainId.Derive(resumeWorkItem.WorkItemId, $"complete:{resumePayload.ActivityExecutionId}"),
            workflowExecutionId: resumeWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(resumeWorkItem.CommandId, $"complete:{resumePayload.ActivityExecutionId}"),
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: resumeWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(resumeWorkItem.IdempotencyKey, $"complete:{resumePayload.ActivityExecutionId}"),
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
        state = EndOpenAttempt(
            state with { TriggerRegistrations = [], BookmarkIds = [] },
            Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault,
            _timeProvider.GetUtcNow(),
            incidentId);
        state = ActivityAttemptActivationClaimer.CompactTriggerDeliveryHistory(state);
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
