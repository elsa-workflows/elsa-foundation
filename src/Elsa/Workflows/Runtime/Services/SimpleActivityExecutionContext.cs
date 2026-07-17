using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Runtime-owned orchestration context used by the engine and structural activities. It exposes no
/// service resolution or mutable result/output/bookmark channel; ordinary activity authors receive
/// <see cref="ActivityExecutionContext"/> instead.
/// </summary>
public sealed class SimpleActivityExecutionContext(
    IActivity activity,
    CancellationToken cancellationToken,
    string? workflowExecutionId = null,
    WorkflowExecutableIdentity? pinnedExecutable = null,
    RuntimeSchedulerWorkItem? schedulerWorkItem = null,
    ExecutableNode? executableNode = null,
    ActivityExecutionState? activityExecutionState = null,
    VariableScope? variableScope = null,
    JsonElement? triggerPayload = null,
    string? triggerNodeId = null,
    string? invocationId = null,
    string? executableNodeId = null)
    : IRuntimeActivityExecutionContext
{
    // The single construction path for a runtime activity context.
    public static SimpleActivityExecutionContext ForExecution(
        IActivity activity,
        CancellationToken cancellationToken,
        string workflowExecutionId,
        WorkflowExecutableIdentity pinnedExecutable,
        RuntimeSchedulerWorkItem? schedulerWorkItem,
        ExecutableNode? executableNode,
        ActivityExecutionState? activityExecutionState,
        VariableScope? variableScope,
        JsonElement? triggerPayload = null,
        string? triggerNodeId = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(pinnedExecutable);

        return new SimpleActivityExecutionContext(
            activity,
            cancellationToken,
            workflowExecutionId,
            pinnedExecutable,
            schedulerWorkItem,
            executableNode,
            activityExecutionState,
            variableScope,
            triggerPayload,
            triggerNodeId);
    }

    private readonly List<RuntimeChildActivityScheduleRequest> _childActivityScheduleRequests = [];

    public IActivity Activity { get; } = activity;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public string WorkflowExecutionId { get; } = workflowExecutionId ?? string.Empty;
    public string InvocationId => invocationId ?? activityExecutionState?.InvocationId ?? throw MissingRuntimeValue(nameof(InvocationId));
    public string AttemptId => activityExecutionState?.Attempts?.LastOrDefault(attempt => attempt.EndedAt is null)?.AttemptId ?? string.Empty;
    public string ExecutableNodeId => executableNodeId ?? executableNode?.ExecutableNodeId ?? throw MissingRuntimeValue(nameof(ExecutableNodeId));
    public JsonElement? TriggerPayload => triggerPayload?.Clone() ?? activityExecutionState?.TriggerDeliveries?
        .LastOrDefault(delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed)?
        .Payload.InlineValue?.Clone();
    public string? TriggerNodeId { get; } = string.IsNullOrWhiteSpace(triggerNodeId) ? null : triggerNodeId;
    public WorkflowExecutableIdentity PinnedExecutable => pinnedExecutable ?? throw MissingRuntimeValue(nameof(PinnedExecutable));
    public RuntimeSchedulerWorkItem SchedulerWorkItem => schedulerWorkItem ?? throw MissingRuntimeValue(nameof(SchedulerWorkItem));
    public ExecutableNode ExecutableNode => executableNode ?? throw MissingRuntimeValue(nameof(ExecutableNode));
    public ActivityExecutionState ActivityExecutionState => activityExecutionState ?? throw MissingRuntimeValue(nameof(ActivityExecutionState));

    public void ScheduleChildActivity(
        string executableNodeId,
        string? schedulingActivityExecutionId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        ActivitySchedulingProvenance? schedulingProvenance = null,
        LoopIterationScopeRequest? iterationFrame = null)
    {
        _childActivityScheduleRequests.Add(new RuntimeChildActivityScheduleRequest(
            executableNodeId,
            schedulingActivityExecutionId,
            metadata,
            schedulingProvenance,
            iterationFrame));
    }

    public IReadOnlyCollection<RuntimeChildActivityScheduleRequest> GetChildActivityScheduleRequests() =>
        _childActivityScheduleRequests.ToArray();

    /// <summary>Projects the engine context to the deliberately smaller ordinary activity context.</summary>
    public ActivityExecutionContext ToActivityExecutionContext() =>
        new(
            WorkflowExecutionId,
            InvocationId,
            AttemptId,
            ExecutableNodeId,
            CancellationToken,
            TriggerPayload,
            TriggerNodeId);

    /// <summary>
    /// The visible container-scope chain threaded by the runtime for this concrete activity execution
    /// (ADR 0027). Null when the activity has no enclosing container scope.
    /// </summary>
    public VariableScope? VariableScope { get; } = variableScope;

    private static InvalidOperationException MissingRuntimeValue(string name) =>
        new($"Runtime activity execution context value '{name}' is unavailable for this context.");

}
