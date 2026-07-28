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
    string? executableNodeId = null,
    IReadOnlyDictionary<string, string>? triggerMetadata = null,
    IReadOnlyCollection<RuntimeLiveChildActivity>? liveChildActivities = null,
    IReadOnlyDictionary<string, ValueEnvelope>? scopedVariableEnvelopes = null)
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
        string? triggerNodeId = null,
        IReadOnlyDictionary<string, string>? triggerMetadata = null,
        IReadOnlyCollection<RuntimeLiveChildActivity>? liveChildActivities = null,
        IReadOnlyDictionary<string, ValueEnvelope>? scopedVariableEnvelopes = null)
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
            triggerNodeId,
            triggerMetadata: triggerMetadata,
            liveChildActivities: liveChildActivities,
            scopedVariableEnvelopes: scopedVariableEnvelopes);
    }

    private readonly List<RuntimeChildActivityScheduleRequest> _childActivityScheduleRequests = [];
    private readonly List<RuntimeChildSubtreeCancellationRequest> _childSubtreeCancellationRequests = [];
    private readonly List<RuntimeChildFaultAbsorptionRequest> _childFaultAbsorptionRequests = [];
    private readonly List<RuntimeParentNotificationRequest> _parentNotificationRequests = [];

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
    public IReadOnlyDictionary<string, string>? TriggerMetadata { get; } = triggerMetadata is { Count: > 0 } ? triggerMetadata : null;
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

    private readonly IReadOnlyCollection<RuntimeLiveChildActivity> _liveChildActivities = liveChildActivities ?? [];

    public IReadOnlyCollection<RuntimeLiveChildActivity> GetLiveChildActivities() => _liveChildActivities;

    // The visible name→committed-envelope projection the runtime built for an IRuntimeScopedVariableReader
    // activity (spec 123 D1). Null for every non-marker activity and for any handler path that did not populate
    // it, so a read there always returns false — no throw-on-unpopulated.
    private readonly IReadOnlyDictionary<string, ValueEnvelope>? _scopedVariableEnvelopes = scopedVariableEnvelopes;

    public bool TryReadScopedVariableValue(string variableName, out ValueEnvelope? envelope)
    {
        envelope = null;
        if (_scopedVariableEnvelopes is null || string.IsNullOrEmpty(variableName))
            return false;
        if (!_scopedVariableEnvelopes.TryGetValue(variableName, out var committed))
            return false;
        envelope = committed;
        return true;
    }

    public void RequestChildSubtreeCancellation(
        string childActivityExecutionId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        _childSubtreeCancellationRequests.Add(new RuntimeChildSubtreeCancellationRequest(
            childActivityExecutionId,
            reason,
            metadata));
    }

    public IReadOnlyCollection<RuntimeChildSubtreeCancellationRequest> GetChildSubtreeCancellationRequests() =>
        _childSubtreeCancellationRequests.ToArray();

    public void RequestChildFaultAbsorption(
        string incidentId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        _childFaultAbsorptionRequests.Add(new RuntimeChildFaultAbsorptionRequest(incidentId, reason, metadata));
    }

    public IReadOnlyCollection<RuntimeChildFaultAbsorptionRequest> GetChildFaultAbsorptionRequests() =>
        _childFaultAbsorptionRequests.ToArray();

    public void RequestParentNotification(string code, JsonElement? payload = null)
    {
        _parentNotificationRequests.Add(new RuntimeParentNotificationRequest(code, payload));
    }

    public IReadOnlyCollection<RuntimeParentNotificationRequest> GetParentNotificationRequests() =>
        _parentNotificationRequests.ToArray();

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
