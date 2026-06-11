using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
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
    private const string CompletionOutcomeNamesMetadataKey = "runtime.completionOutcomeNames";

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
        var activityFactory = scope.ServiceProvider.GetRequiredService<IActivityFactory>();
        var schedulerWorkQueue = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();

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

        if (state.Status == ActivityExecutionStatus.Completed)
        {
            await EnqueueCompletionWorkAsync(schedulerWorkQueue, workItem, resumePayload, state, cancellationToken);
            return;
        }

        if (state.Status is ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled)
            return;

        await ResumeActivityAsync(scope.ServiceProvider, activityFactory, activityExecutionStateStore, schedulerWorkQueue, workItem, resumePayload, executableNode, state, cancellationToken);
    }

    private async ValueTask ResumeActivityAsync(
        IServiceProvider serviceProvider,
        IActivityFactory activityFactory,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RuntimeMaterializedActivityInput> inputs;
        try
        {
            inputs = _inputMaterializer.MaterializeInputs(executableNode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await activityExecutionStateStore.SaveAsync(FaultActivity(workItem, resumePayload, state, exception, "InputMaterializationFailed"), cancellationToken);
            return;
        }

        var activity = await activityFactory.Create(
            executableNode.DescriptorType,
            executableNode.DescriptorPayload,
            inputs.ToDictionary(input => input.Name, input => input.Argument, StringComparer.OrdinalIgnoreCase),
            outputs: null,
            cancellationToken);

        activity.NodeId = executableNode.ExecutableNodeId;
        activity.Id = resumePayload.ActivityExecutionId;

        var context = new SimpleActivityExecutionContext(serviceProvider, activity, cancellationToken);
        RuntimeActivityInputMemory.Seed(context, inputs);

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
            await activityExecutionStateStore.SaveAsync(FaultActivity(workItem, resumePayload, state, exception, "ActivityResumeFaulted"), cancellationToken);
            return;
        }

        var completedState = CompleteActivity(workItem, resumePayload, state, NormalizeOutcomeNames(context.GetOutcomes(), defaultToDone: true));
        await activityExecutionStateStore.SaveAsync(completedState, cancellationToken);
        await EnqueueCompletionWorkAsync(schedulerWorkQueue, workItem, resumePayload, completedState, cancellationToken);
    }

    private async ValueTask EnqueueCompletionWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem resumeWorkItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState completedState,
        CancellationToken cancellationToken)
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

        var workItem = new RuntimeSchedulerWorkItem(
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

        await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
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
        metadata["runtime.resumeReason"] = resumePayload.Reason;
        metadata["runtime.resumeSchedulerWorkItemId"] = workItem.WorkItemId;
        metadata["runtime.bookmarkId"] = resumePayload.BookmarkId;
        metadata["runtime.resumeTargetId"] = resumePayload.ResumeTargetId;
        metadata[CompletionOutcomeNamesMetadataKey] = JsonSerializer.Serialize(outcomeNames);

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

    private ActivityExecutionState FaultActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus)
    {
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["runtime.resumeReason"] = resumePayload.Reason;
        metadata["runtime.resumeSchedulerWorkItemId"] = workItem.WorkItemId;
        metadata["runtime.bookmarkId"] = resumePayload.BookmarkId;
        metadata["runtime.resumeTargetId"] = resumePayload.ResumeTargetId;
        metadata["runtime.faultType"] = exception.GetType().FullName ?? exception.GetType().Name;
        metadata["runtime.faultMessage"] = exception.Message;

        return state with
        {
            Status = ActivityExecutionStatus.Faulted,
            SubStatus = subStatus,
            CompletedAt = _timeProvider.GetUtcNow(),
            FaultCount = state.FaultCount + 1,
            AggregateFaultCount = state.AggregateFaultCount + 1,
            Metadata = metadata
        };
    }
}
