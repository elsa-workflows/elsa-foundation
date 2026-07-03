using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
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

    private readonly IRuntimeActivityInputMaterializer _inputMaterializer;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeProvider _timeProvider;

    public WorkflowResumeBookmarkSchedulerWorkHandler(
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
        var activityFactory = scope.ServiceProvider.GetRequiredService<IActivityFactory>();
        var schedulerWorkQueue = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var checkpointCommitter = scope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>();
        var activityFaultIncidentRecorder = scope.ServiceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        var activityOutputRegister = scope.ServiceProvider.GetRequiredService<IRuntimeActivityOutputRegister>();
        var durableValueStateStore = scope.ServiceProvider.GetRequiredService<IDurableValueStateStore>();
        var payloadCapturePolicy = scope.ServiceProvider.GetService<IRuntimePayloadCapturePolicy>() ?? new DefaultRuntimePayloadCapturePolicy();

        var executable = await workflowExecutableStore.FindAsync(resumePayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(resumePayload.PinnedExecutable.ArtifactId);

        ValidatePinnedExecutable(workItem, resumePayload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.TryGetValue(resumePayload.ExecutableNodeId, out var executableNode))
            throw new InvalidOperationException($"ResumeBookmark scheduler work item '{workItem.WorkItemId}' references executable node '{resumePayload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

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

        if (state.Status is ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Recovered)
            return;

        if (bookmark is null)
            return;

        ValidateBookmarkMatchesPayload(workItem, resumePayload, bookmark);

        await ResumeActivityAsync(scope.ServiceProvider, activityFactory, checkpointCommitter, activityFaultIncidentRecorder, bookmarkConsumptionCheckpointService, schedulerWorkQueue, activityOutputRegister, durableValueStateStore, payloadCapturePolicy, workItem, resumePayload, bookmark, executable, executableNode, state, cancellationToken);
    }

    private async ValueTask ResumeActivityAsync(
        IServiceProvider serviceProvider,
        IActivityFactory activityFactory,
        RuntimeCheckpointCommitter checkpointCommitter,
        ActivityFaultIncidentRecorder activityFaultIncidentRecorder,
        IBookmarkConsumptionCheckpointService bookmarkConsumptionCheckpointService,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IRuntimeActivityOutputRegister activityOutputRegister,
        IDurableValueStateStore durableValueStateStore,
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        BookmarkState bookmark,
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RuntimeMaterializedActivityInput> inputs;
        VariableScope? variableScope;
        IReadOnlyDictionary<string, object?> workflowVariables;
        IReadOnlyDictionary<string, object?> workflowInputValues;
        IReadOnlyDictionary<string, object?> activityOutputValues;
        // Hoisted out of the try (like the invoke path) so they populate the resumed activity's execution-time
        // expression carrier (ADR 0030) below, projected from the IdentityName-tagged durable values this resume
        // already re-lists (spec 083 review) — so the RESUMED activity's own getCorrelationId() /
        // getWorkflowInstanceName() observe the current identity too, without a workflow-execution-state read.
        string? carrierCorrelationId;
        string? carrierInstanceName;
        try
        {
            var durableValues = await durableValueStateStore.ListAsync(workItem.WorkflowExecutionId, cancellationToken);
            workflowVariables = RuntimeInputBindingStateProjection.ProjectWorkflowVariables(durableValues);
            workflowInputValues = RuntimeInputBindingStateProjection.ProjectWorkflowInputs(durableValues);
            activityOutputValues = RuntimeInputBindingStateProjection.ProjectActivityOutputValues(durableValues);
            carrierCorrelationId = RuntimeIdentityStateProjection.ProjectCorrelationId(durableValues);
            carrierInstanceName = RuntimeIdentityStateProjection.ProjectInstanceName(durableValues);

            // Build the visible container-scope chain (ADR 0027) anchored from the current durable-value variable
            // projection, so a resume callback's freehand expressions read container/workflow-scoped variables and
            // in-evaluation write-back lands in the declaring scope — parity with the invoke path. (Durable
            // persistence of resume-time variable mutations across the bookmark-consumption checkpoint is a
            // separate follow-up: BookmarkConsumptionCheckpointRequest does not yet carry durable-value changes.)
            var scopeService = new RuntimeContainerScopeService(serviceProvider.GetRequiredService<IActivityExecutionStateStore>());
            variableScope = await scopeService.BuildScopeAsync(executable, workItem.WorkflowExecutionId, state, cancellationToken, workflowVariables);

            var resolutionContext = new RuntimeInputBindingResolutionContext(
                workflowExecutionId: workItem.WorkflowExecutionId,
                activityExecutionId: resumePayload.ActivityExecutionId,
                durableValuesByValueId: durableValues.ToDictionary(value => value.ValueId, StringComparer.Ordinal),
                activityOutputs: activityOutputRegister,
                serviceProvider: serviceProvider,
                workflowVariables: workflowVariables,
                workflowInputs: workflowInputValues,
                activityOutputValues: activityOutputValues,
                variableScope: variableScope);
            inputs = await _inputMaterializer.MaterializeInputsAsync(executableNode, resolutionContext, cancellationToken);
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
        try
        {
            valueSnapshots.AddRange(BuildInputValueSnapshots(payloadCapturePolicy, workItem, resumePayload, inputs, _timeProvider.GetUtcNow()));

            // Activity construction + argument binding (ActivityArgumentBinder, invoked inside Create) runs
            // inside a fault boundary on the resume path too (#325, sibling of #317). Previously this step sat
            // between the input-materialization try/catch and the resume-execution try/catch, so a binder/constructor
            // throw escaped to the scheduler loop and left the run silently at Running with no incident. Recording it
            // as a blocking incident faults the activity and surfaces a queryable cause, distinct from
            // InputMaterializationFailed and the ActivityResumeFaulted resume-method failure below.
            activity = await activityFactory.Create(
                executableNode.DescriptorType,
                executableNode.DescriptorPayload,
                inputs.ToDictionary(input => input.Name, input => input.Argument, StringComparer.OrdinalIgnoreCase),
                BuildOutputArguments(executableNode),
                cancellationToken);

            activity.NodeId = executableNode.ExecutableNodeId;
            activity.Id = resumePayload.ActivityExecutionId;

            // Populate the live execution-time expression carrier (ADR 0030) for the resume callback via the shared
            // helper: identity from the IdentityName-tagged durable-value projection (spec 083 review — no per-resume
            // workflow-execution-state read, and a Correlate/SetName in this run is visible here too), the visible
            // variable scope, and the durable-value projections for inputs/variables/outputs. Previously the resume
            // context was built with none of these, so a resume callback that evaluated JavaScript/Liquid saw empty
            // getWorkflowInstanceId()/getInput()/getVariable()/getOutput() and had no scope to write variables into.
            var carrier = RuntimeExecutionExpressionCarrier.Create(
                carrierCorrelationId, carrierInstanceName, resumePayload.PinnedExecutable, workflowInputValues, workflowVariables, activityOutputValues);
            context = new SimpleActivityExecutionContext(
                serviceProvider,
                activity,
                cancellationToken,
                workItem.WorkflowExecutionId,
                resumePayload.PinnedExecutable,
                workItem,
                executableNode,
                state,
                variableScope,
                correlationId: carrier.CorrelationId,
                workflowName: carrier.WorkflowName,
                workflowDefinitionVersion: carrier.WorkflowDefinitionVersion,
                workflowInputs: carrier.WorkflowInputs,
                workflowVariables: carrier.WorkflowVariables,
                activityOutputValues: carrier.ActivityOutputValues);
            RuntimeActivityInputMemory.Seed(context, inputs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordFaultAsync(serviceProvider, activityFaultIncidentRecorder, checkpointCommitter, workItem, resumePayload, state, exception, "ActivityResumeConstructionFailed", valueSnapshots, cancellationToken);
            return;
        }

        try
        {
            var resumeMethod = ResolveResumeMethod(activity.GetType(), resumePayload.ResumeTargetId);
            await InvokeResumeMethodAsync(resumeMethod, activity, context, resumePayload.Input, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            valueSnapshots.AddRange(BuildOutputValueSnapshots(payloadCapturePolicy, workItem, resumePayload, executableNode, context.GetRecordedOutputs(), _timeProvider.GetUtcNow()));
            await RecordFaultAsync(serviceProvider, activityFaultIncidentRecorder, checkpointCommitter, workItem, resumePayload, state, exception, "ActivityResumeFaulted", valueSnapshots, cancellationToken);
            return;
        }

        valueSnapshots.AddRange(BuildOutputValueSnapshots(payloadCapturePolicy, workItem, resumePayload, executableNode, context.GetRecordedOutputs(), _timeProvider.GetUtcNow()));
        var completedState = CompleteActivity(workItem, resumePayload, state, NormalizeOutcomeNames(context.GetOutcomes(), defaultToDone: true));
        await bookmarkConsumptionCheckpointService.CommitAsync(new BookmarkConsumptionCheckpointRequest(workItem, resumePayload, bookmark, completedState, NewCompletionWorkItem(workItem, resumePayload, completedState), valueSnapshots), cancellationToken);
    }

    private static IDictionary<string, OutputArgument> BuildOutputArguments(ExecutableNode executableNode) =>
        executableNode.OutputCaptures.ToDictionary(
            item => item.Key,
            item => (OutputArgument)new OutputArgument<object?>(new RuntimeOutputMemoryBlockReference(item.Key)),
            StringComparer.Ordinal);

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

    private static RuntimeResumeBookmarkCommandPayload DeserializeResumePayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("ResumeBookmark scheduler work item requires a resume bookmark payload.");

        try
        {
            return payload.Deserialize<RuntimeResumeBookmarkCommandPayload>()
                   ?? throw new InvalidOperationException("ResumeBookmark scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsResumePayloadValidationException(argumentException))
        {
            throw new InvalidOperationException("ResumeBookmark scheduler work item payload is not a valid resume bookmark payload.", exception);
        }
    }

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

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"ResumeBookmark scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

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

    private static MethodInfo ResolveResumeMethod(Type activityType, string resumeTargetId)
    {
        var methods = activityType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.GetCustomAttribute<ResumeTargetAttribute>()?.ResumeTargetId == resumeTargetId)
            .ToArray();

        return methods.Length switch
        {
            0 => throw new InvalidOperationException($"Activity type '{activityType.FullName}' does not declare resume target '{resumeTargetId}'."),
            1 => ValidateResumeMethod(methods[0]),
            _ => throw new InvalidOperationException($"Activity type '{activityType.FullName}' declares resume target '{resumeTargetId}' more than once.")
        };
    }

    private static MethodInfo ValidateResumeMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var hasSupportedParameter =
            parameters.Length == 0 ||
            parameters.Length == 1 && (parameters[0].ParameterType == typeof(IActivityExecutionContext) || parameters[0].ParameterType == typeof(JsonElement));
        var hasSupportedReturn =
            method.ReturnType == typeof(void) ||
            method.ReturnType == typeof(Task) ||
            method.ReturnType == typeof(ValueTask);

        if (!hasSupportedParameter || !hasSupportedReturn)
            throw new InvalidOperationException($"Resume target method '{method.DeclaringType?.FullName}.{method.Name}' has an unsupported signature.");

        return method;
    }

    private static async ValueTask InvokeResumeMethodAsync(
        MethodInfo method,
        IActivity activity,
        IActivityExecutionContext context,
        JsonElement? input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parameters = method.GetParameters();
        object?[]? arguments = parameters.Length == 0
            ? null
            : parameters[0].ParameterType == typeof(IActivityExecutionContext)
                ? [context]
                : [input ?? default(JsonElement)];
        object? result;
        try
        {
            result = method.Invoke(activity, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        switch (result)
        {
            case null:
                return;
            case Task task:
                await task.WaitAsync(cancellationToken);
                return;
            case ValueTask valueTask:
                await valueTask.AsTask().WaitAsync(cancellationToken);
                return;
            default:
                throw new InvalidOperationException($"Resume target method '{method.DeclaringType?.FullName}.{method.Name}' returned unsupported result type '{result.GetType().FullName}'.");
        }
    }

    private ActivityExecutionState CompleteActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState state,
        IReadOnlyCollection<string> outcomeNames)
    {
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ResumeReason] = resumePayload.Reason;
        metadata[RuntimeMetadataKeys.ResumeSchedulerWorkItemId] = workItem.WorkItemId;
        metadata[RuntimeMetadataKeys.BookmarkId] = resumePayload.BookmarkId;
        metadata[RuntimeMetadataKeys.ResumeTargetId] = resumePayload.ResumeTargetId;
        metadata[RuntimeMetadataKeys.CompletionOutcomeNames] = JsonSerializer.Serialize(outcomeNames);

        return state with
        {
            Status = ActivityExecutionStatus.Completed,
            SubStatus = null,
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
        var request = NewFaultIncidentRecordRequest(checkpointCommitter, workItem, resumePayload, state, exception, subStatus, valueSnapshots);
        var incidentId = ActivityFaultIncidentRecorder.IncidentId(workItem.WorkItemId, resumePayload.ActivityExecutionId, subStatus);
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

    private static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> BuildInputValueSnapshots(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
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
                    activityExecutionId: resumePayload.ActivityExecutionId,
                    valueName: input.Name,
                    type: type,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.ExecutableNodeId] = resumePayload.ExecutableNodeId,
                        [RuntimeMetadataKeys.ResumeSchedulerWorkItemId] = workItem.WorkItemId
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
        RuntimeResumeBookmarkCommandPayload resumePayload,
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
                    activityExecutionId: resumePayload.ActivityExecutionId,
                    valueName: output.OutputName,
                    type: type,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.ExecutableNodeId] = resumePayload.ExecutableNodeId,
                        [RuntimeMetadataKeys.ResumeSchedulerWorkItemId] = workItem.WorkItemId
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

    private static JsonElement SerializeValue(object? value) =>
        value is JsonElement json
            ? json.Clone()
            : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));

    private static JsonElement? SerializeCapturedValue(RuntimePayloadCaptureDecision decision, object? value) =>
        decision.CapturesPayload ? SerializeValue(value) : null;

    private static RuntimeValueTypeDescriptor RuntimeObjectType { get; } = new("clr", typeof(object).FullName, null);

    private static RuntimeValueTypeDescriptor TypeDescriptorFor(object? value) =>
        value is null ? RuntimeObjectType : new RuntimeValueTypeDescriptor("clr", value.GetType().FullName, null);

    private sealed class RuntimeOutputMemoryBlockReference(string id) : IMemoryBlockReference
    {
        public string Id { get; set; } = id;

        public IMemoryBlock Declare() => new RuntimeOutputMemoryBlock();

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) =>
            context.Get<T>(this);

        public T? Get<T>(IExpressionExecutionContext context) =>
            context.Get<T>(this);

        public void Set<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context, T? value) =>
            context.Set(this, value);
    }

    private sealed class RuntimeOutputMemoryBlock : IMemoryBlock
    {
        public object? Value { get; set; }
        public object? Metadata { get; set; }
    }
}
