using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class SimpleActivityExecutionContext(
    IServiceProvider serviceProvider,
    IActivity activity,
    CancellationToken cancellationToken,
    string? workflowExecutionId = null,
    WorkflowExecutableIdentity? pinnedExecutable = null,
    RuntimeSchedulerWorkItem? schedulerWorkItem = null,
    ExecutableNode? executableNode = null,
    ActivityExecutionState? activityExecutionState = null,
    VariableScope? variableScope = null,
    JsonElement? triggerPayload = null,
    string? triggerNodeId = null)
    : IRuntimeActivityExecutionContext, IActivityInvocationIdentity
{
    // The single construction path for a runtime activity context.
    public static SimpleActivityExecutionContext ForExecution(
        IServiceProvider serviceProvider,
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
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(pinnedExecutable);

        return new SimpleActivityExecutionContext(
            serviceProvider,
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

    private readonly List<string> _outcomes = [];
    private readonly List<ActivityBookmarkRequest> _bookmarkRequests = [];
    private readonly List<RuntimeChildActivityScheduleRequest> _childActivityScheduleRequests = [];
    private readonly List<string> _compositeCompletionOutcomeNames = [];
    private readonly List<string> _finishWorkflowOutcomeNames = [];

    public IActivity Activity { get; } = activity;
    public IActivityExecutionContext ParentActivityExecutionContext => null!;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public string WorkflowExecutionId { get; } = workflowExecutionId ?? string.Empty;
    public string InvocationId => activityExecutionState?.InvocationId ?? Activity.Id;
    public string AttemptId => activityExecutionState?.Attempts?.LastOrDefault(attempt => attempt.EndedAt is null)?.AttemptId ?? string.Empty;
    public string ExecutableNodeId => executableNode?.ExecutableNodeId ?? Activity.NodeId;
    public JsonElement? TriggerPayload => triggerPayload?.Clone() ?? activityExecutionState?.TriggerDeliveries?
        .LastOrDefault(delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed)?
        .Payload.InlineValue?.Clone();
    public string? TriggerNodeId { get; } = string.IsNullOrWhiteSpace(triggerNodeId) ? null : triggerNodeId;
    public WorkflowExecutableIdentity PinnedExecutable => pinnedExecutable ?? throw MissingRuntimeValue(nameof(PinnedExecutable));
    public RuntimeSchedulerWorkItem SchedulerWorkItem => schedulerWorkItem ?? throw MissingRuntimeValue(nameof(SchedulerWorkItem));
    public ExecutableNode ExecutableNode => executableNode ?? throw MissingRuntimeValue(nameof(ExecutableNode));
    public ActivityExecutionState ActivityExecutionState => activityExecutionState ?? throw MissingRuntimeValue(nameof(ActivityExecutionState));
    public bool CompositeCompletionRequested { get; private set; }
    public bool CompositeCompletionDeferred { get; private set; }
    public IReadOnlyCollection<string> CompositeCompletionOutcomeNames => _compositeCompletionOutcomeNames.ToArray();
    public bool FinishWorkflowRequested { get; private set; }
    public IReadOnlyCollection<string> FinishWorkflowOutcomeNames => _finishWorkflowOutcomeNames.ToArray();
    public bool CorrelationIdAssignmentRequested { get; private set; }
    public string? RequestedCorrelationId { get; private set; }
    public bool InstanceNameAssignmentRequested { get; private set; }
    public string? RequestedInstanceName { get; private set; }
    private readonly Dictionary<string, object?> _requestedWorkflowOutputs = new(StringComparer.Ordinal);
    public bool WorkflowOutputAssignmentRequested { get; private set; }
    public IReadOnlyDictionary<string, object?> RequestedWorkflowOutputs => new Dictionary<string, object?>(_requestedWorkflowOutputs, StringComparer.Ordinal);

    public TService GetRequiredService<TService>() where TService : notnull =>
        (TService)(serviceProvider.GetService(typeof(TService))
            ?? throw new InvalidOperationException($"Required service '{typeof(TService).FullName}' is not registered."));

    public IAsyncEnumerable<ActivityOutputs> GetActivityOutputs() => AsyncEnumerable.Empty<ActivityOutputs>();

    public void SetOutcomes(string[] outcomes)
    {
        _outcomes.Clear();
        _outcomes.AddRange(outcomes);
    }

    public IEnumerable<string> GetOutcomes() => _outcomes;

    public void CreateBookmark(ActivityBookmarkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_bookmarkRequests.Any(existing => StringComparer.Ordinal.Equals(existing.BookmarkId, request.BookmarkId)))
            throw new InvalidOperationException($"Bookmark request '{request.BookmarkId}' is already registered for this activity execution.");

        _bookmarkRequests.Add(request);
    }

    public IReadOnlyCollection<ActivityBookmarkRequest> GetBookmarkRequests() => _bookmarkRequests.ToArray();

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

    public void CompleteCompositeActivity(IEnumerable<string>? outcomeNames = null)
    {
        var outcomeSnapshot = (outcomeNames ?? [ActivityOutcomes.Done]).ToArray();
        if (outcomeSnapshot.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Composite completion outcome names cannot contain blank values.");

        if (outcomeSnapshot.Distinct(StringComparer.Ordinal).Count() != outcomeSnapshot.Length)
            throw new InvalidOperationException("Composite completion outcome names cannot contain duplicates.");

        CompositeCompletionRequested = true;
        CompositeCompletionDeferred = false;
        _compositeCompletionOutcomeNames.Clear();
        _compositeCompletionOutcomeNames.AddRange(outcomeSnapshot);
        _outcomes.Clear();
        _outcomes.AddRange(outcomeSnapshot);
    }

    public void DeferCompositeCompletion()
    {
        if (CompositeCompletionRequested)
            throw new InvalidOperationException("Composite completion cannot be deferred after completion was requested.");

        CompositeCompletionDeferred = true;
    }

    public void FinishWorkflow(IEnumerable<string>? outcomeNames = null)
    {
        var outcomeSnapshot = (outcomeNames ?? [ActivityOutcomes.Done]).ToArray();
        if (outcomeSnapshot.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Finish workflow outcome names cannot contain blank values.");

        if (outcomeSnapshot.Distinct(StringComparer.Ordinal).Count() != outcomeSnapshot.Length)
            throw new InvalidOperationException("Finish workflow outcome names cannot contain duplicates.");

        FinishWorkflowRequested = true;
        _finishWorkflowOutcomeNames.Clear();
        _finishWorkflowOutcomeNames.AddRange(outcomeSnapshot);
    }

    public void SetCorrelationId(string? correlationId)
    {
        CorrelationIdAssignmentRequested = true;
        RequestedCorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId;
    }

    public void SetInstanceName(string? instanceName)
    {
        InstanceNameAssignmentRequested = true;
        RequestedInstanceName = string.IsNullOrWhiteSpace(instanceName) ? null : instanceName;
    }

    public void SetWorkflowOutput(string outputName, object? value)
    {
        if (string.IsNullOrWhiteSpace(outputName))
            return;

        WorkflowOutputAssignmentRequested = true;
        _requestedWorkflowOutputs[outputName] = value;
    }

    /// <summary>
    /// The visible container-scope chain threaded by the runtime for this concrete activity execution
    /// (ADR 0027). Null when the activity has no enclosing container scope.
    /// </summary>
    public VariableScope? VariableScope { get; } = variableScope;

    private static InvalidOperationException MissingRuntimeValue(string name) =>
        new($"Runtime activity execution context value '{name}' is unavailable for this context.");

}
