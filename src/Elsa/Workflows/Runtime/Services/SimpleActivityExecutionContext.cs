using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
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
    VariableScope? variableScope = null)
    : IRuntimeActivityExecutionContext, IWorkflowDispatchStagingContext, IExpressionExecutionContext, IScopedVariableProvider, IExecutionExpressionState
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyExpressionState = new Dictionary<string, object?>(StringComparer.Ordinal);

    // The live execution-time expression carrier (ADR 0030), or null for a lightweight/non-execution context.
    // Populated only through ForExecution, which takes the carrier as a single non-nullable value: there is no
    // way to supply a partial or empty carrier, closing the empty-accessor footgun where a caller hand-spread the
    // six carrier fields and could silently omit some. The primary constructor deliberately keeps no carrier
    // parameter — a context built through it (e.g. a non-execution test context) simply has no carrier and its
    // accessors return empty. See RuntimeExecutionExpressionCarrier.
    private readonly RuntimeExecutionExpressionCarrierState? _executionCarrier;

    // The single construction path for an execution-time context (leaf invoke, resume callback, composite
    // child-completion). Requires the resolved carrier state — a non-nullable value type, so it is always
    // present — alongside the runtime identity handles and (optionally) the visible variable scope. All three
    // scheduler work handlers route through here so identity, inputs, variables, and prior activity outputs are
    // populated identically and no production site can reproduce the empty-accessor bug (ADR 0030).
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
        RuntimeExecutionExpressionCarrierState executionCarrier)
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
            executionCarrier);
    }

    // Carrier-bearing constructor invoked only by ForExecution; chains the primary constructor for the shared
    // runtime handles and then pins the ADR 0030 carrier state. The runtime handles mirror the primary
    // constructor's nullability so throw-on-access members (SchedulerWorkItem, ExecutableNode, ...) stay lazy.
    private SimpleActivityExecutionContext(
        IServiceProvider serviceProvider,
        IActivity activity,
        CancellationToken cancellationToken,
        string workflowExecutionId,
        WorkflowExecutableIdentity pinnedExecutable,
        RuntimeSchedulerWorkItem? schedulerWorkItem,
        ExecutableNode? executableNode,
        ActivityExecutionState? activityExecutionState,
        VariableScope? variableScope,
        RuntimeExecutionExpressionCarrierState executionCarrier)
        : this(serviceProvider, activity, cancellationToken, workflowExecutionId, pinnedExecutable, schedulerWorkItem, executableNode, activityExecutionState, variableScope)
    {
        _executionCarrier = executionCarrier;
    }

    private readonly IMemoryRegister _memory = new SimpleMemoryRegister();
    private readonly List<string> _outcomes = [];
    private readonly List<ActivityBookmarkRequest> _bookmarkRequests = [];
    private readonly List<RecordedActivityOutput> _recordedOutputs = [];
    private readonly List<RuntimeChildActivityScheduleRequest> _childActivityScheduleRequests = [];
    private readonly List<string> _compositeCompletionOutcomeNames = [];
    private readonly List<string> _finishWorkflowOutcomeNames = [];

    public IExpressionExecutionContext ExpressionExecutionContext => this;
    public IActivity Activity { get; } = activity;
    public IActivityExecutionContext ParentActivityExecutionContext => null!;
    public IMemoryRegister Memory => _memory;
    public IExpressionExecutionContext? ParentContext { get; set; }
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public string WorkflowExecutionId { get; } = workflowExecutionId ?? string.Empty;
    public WorkflowExecutableIdentity PinnedExecutable => pinnedExecutable ?? throw MissingRuntimeValue(nameof(PinnedExecutable));
    public RuntimeSchedulerWorkItem SchedulerWorkItem => schedulerWorkItem ?? throw MissingRuntimeValue(nameof(SchedulerWorkItem));
    public ExecutableNode ExecutableNode => executableNode ?? throw MissingRuntimeValue(nameof(ExecutableNode));
    public ActivityExecutionState ActivityExecutionState => activityExecutionState ?? throw MissingRuntimeValue(nameof(ActivityExecutionState));
    public bool CompositeCompletionRequested { get; private set; }
    public bool CompositeCompletionDeferred { get; private set; }
    public IReadOnlyCollection<string> CompositeCompletionOutcomeNames => _compositeCompletionOutcomeNames.ToArray();
    public bool FinishWorkflowRequested { get; private set; }
    public IReadOnlyCollection<string> FinishWorkflowOutcomeNames => _finishWorkflowOutcomeNames.ToArray();
    public WorkflowDispatchCheckpointRequest? WorkflowDispatchRequest { get; private set; }
    public bool CorrelationIdAssignmentRequested { get; private set; }
    public string? RequestedCorrelationId { get; private set; }
    public bool InstanceNameAssignmentRequested { get; private set; }
    public string? RequestedInstanceName { get; private set; }
    private readonly Dictionary<string, object?> _requestedWorkflowOutputs = new(StringComparer.Ordinal);
    public bool WorkflowOutputAssignmentRequested { get; private set; }
    public IReadOnlyDictionary<string, object?> RequestedWorkflowOutputs => new Dictionary<string, object?>(_requestedWorkflowOutputs, StringComparer.Ordinal);

    public TService GetRequiredService<TService>() where TService : notnull =>
        (TService)GetRequiredService(typeof(TService))!;

    public object? GetRequiredService(Type type) => serviceProvider.GetService(type)
        ?? throw new InvalidOperationException($"Required service '{type.FullName}' is not registered.");

    public T? Get<T>(InputArgument<T>? input)
    {
        if (input is null)
            return default;

        return Get<T>(input.MemoryBlockReference());
    }

    public void Set<T>(OutputArgument<T>? output, T? value, string? outputName = null)
    {
        if (output is null)
            return;

        var blockReference = output.MemoryBlockReference();
        Set(blockReference, value);
        RecordOutput(ResolveOutputName(outputName, blockReference.Id), value);
    }

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

    public IReadOnlyCollection<RecordedActivityOutput> GetRecordedOutputs() => _recordedOutputs.ToArray();

    public void ScheduleChildActivity(
        string executableNodeId,
        string? schedulingActivityExecutionId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        ActivitySchedulingProvenance? schedulingProvenance = null)
    {
        _childActivityScheduleRequests.Add(new RuntimeChildActivityScheduleRequest(
            executableNodeId,
            schedulingActivityExecutionId,
            metadata,
            schedulingProvenance));
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

    public void StageWorkflowDispatch(WorkflowDispatchCheckpointRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (WorkflowDispatchRequest is not null)
            throw new InvalidOperationException("An activity execution can stage only one workflow dispatch checkpoint request.");

        WorkflowDispatchRequest = request;
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
    /// (ADR 0027). Null when the activity has no enclosing container scope; in that case variable
    /// access falls back to the activity's own memory register.
    /// </summary>
    public VariableScope? VariableScope { get; } = variableScope;

    public bool IsContainedWithinCompositeActivity() => false;
    public bool TryGetActivityInput(string key, out object? value) => TryGetById(key, out value);
    public bool TryGetWorkflowInput(string key, out object? value) => TryGetById(key, out value);

    public object? GetVariableValueOrDefault(string variableName) =>
        VariableScope is { } scope && scope.TryGetValueByName(variableName, out var value) ? value : null;
    public string GetCorrelationId() => CorrelationId ?? string.Empty;
    public string GetWorkflowDefinitionId() => WorkflowDefinitionId;
    public string GetWorkflowDefinitionVersionId() => WorkflowDefinitionVersionId;
    public int GetWorkflowDefinitionVersion() => WorkflowDefinitionVersion;
    public string GetWorkflowInstanceId() => WorkflowInstanceId;

    // IExecutionExpressionState — the live execution-time expression carrier (ADR 0030). Populated by the
    // scheduler work handler from runtime state; read by the JavaScript pre/post-processors after casting the
    // passed IExpressionExecutionContext. Identity is sourced from WorkflowExecutionState + the pinned
    // executable; inputs/variables/outputs are the durable-value projections. No Design dependency (§E2.2/§E2.6).
    public string WorkflowInstanceId => WorkflowExecutionId;
    // Reflect a pending script assignment (setCorrelationId/setWorkflowInstanceName) so a read-after-write within
    // the same evaluation observes the new value, even though the durable change is folded at the checkpoint
    // via the control-leaf intent path (spec 064 scenario 3, carried forward by ADR 0030).
    public string? CorrelationId => CorrelationIdAssignmentRequested ? RequestedCorrelationId : _executionCarrier?.CorrelationId;
    public string? WorkflowName => InstanceNameAssignmentRequested ? RequestedInstanceName : _executionCarrier?.WorkflowName;
    public string WorkflowDefinitionId => pinnedExecutable?.DefinitionId ?? string.Empty;
    public string WorkflowDefinitionVersionId => pinnedExecutable?.DefinitionVersionId ?? string.Empty;
    public int WorkflowDefinitionVersion => _executionCarrier?.WorkflowDefinitionVersion ?? 0;
    public IReadOnlyDictionary<string, object?> WorkflowInputs => _executionCarrier?.WorkflowInputs ?? EmptyExpressionState;
    public object? StimulusInput => _executionCarrier?.StimulusInput;
    public string? TriggerNodeId => _executionCarrier?.TriggerNodeId;
    public JsonElement? ResumeInput => _executionCarrier?.ResumeInput;
    public IReadOnlyDictionary<string, object?> WorkflowVariables => _executionCarrier?.WorkflowVariables ?? EmptyExpressionState;
    public IReadOnlyDictionary<string, object?> ActivityOutputValues => _executionCarrier?.ActivityOutputValues ?? EmptyExpressionState;

    public IMemoryBlock GetBlock(IMemoryBlockReference blockReference) => _memory.Declare(blockReference);
    public bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block) => _memory.TryGetBlock(blockReference.Id, out block);
    public T? Get<T>(IMemoryBlockReference blockReference) => ConvertValue<T>(GetBlock(blockReference).Value);

    public void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null)
    {
        var block = _memory.Declare(blockReference);
        block.Value = value;
        configure?.Invoke(block);
    }

    public IVariable? GetVariable(string name, bool localScopeOnly = false) =>
        VariableScope?.ResolveByName(name);

    public IVariable SetVariable<T>(string name, T? value, Action<IMemoryBlock>? configure = null)
    {
        // Prefer the visible scope chain so an assignment lands in the declaring container scope and is
        // observed by sibling branches; fall back to local memory when no scope declares the name.
        if (VariableScope is { } scope && scope.TrySetValueByName(name, value))
            return scope.ResolveByName(name) ?? new SimpleVariable(name, value);

        var variable = new SimpleVariable(name, value);
        Set(variable, value, configure);
        return variable;
    }

    public IEnumerable<IVariable> EnumerateVariablesInScope() =>
        VariableScope?.EnumerateVisibleVariables() ?? [];

    // IScopedVariableProvider — resolves structured/name-based variable access through the visible
    // scope chain so variable-assignment activities and scripts read and write container-scoped
    // variables in production (ADR 0027). Returns false/empty when no scope chain is present.
    public bool TryGetScopedVariableValue(VariableReference reference, out object? value)
    {
        if (VariableScope is { } scope && scope.TryGetValue(reference, out value))
            return true;

        value = null;
        return false;
    }

    public bool TrySetScopedVariableValue(VariableReference reference, object? value) =>
        VariableScope?.TrySetValue(reference, value) ?? false;

    public IReadOnlyCollection<IVariable> GetVisibleVariables() =>
        VariableScope?.EnumerateVisibleVariables() ?? [];

    public bool TryGetVariableValueByName(string name, out object? value)
    {
        if (VariableScope is { } scope && scope.TryGetValueByName(name, out value))
            return true;

        value = null;
        return false;
    }

    public bool TrySetVariableValueByName(string name, object? value) =>
        VariableScope?.TrySetValueByName(name, value) ?? false;

    private static InvalidOperationException MissingRuntimeValue(string name) =>
        new($"Runtime activity execution context value '{name}' is unavailable for this context.");

    private bool TryGetById(string key, out object? value)
    {
        if (_memory.Blocks.TryGetValue(key, out var block))
        {
            value = block.Value;
            return true;
        }

        value = null;
        return false;
    }

    private void RecordOutput(string outputName, object? value)
    {
        _recordedOutputs.RemoveAll(existing => StringComparer.Ordinal.Equals(existing.OutputName, outputName));
        _recordedOutputs.Add(new RecordedActivityOutput(outputName, value));
    }

    private static string ResolveOutputName(string? outputName, string memoryBlockId)
    {
        var resolved = string.IsNullOrWhiteSpace(memoryBlockId) ? outputName?.Trim() ?? string.Empty : memoryBlockId.Trim();
        var lastMemberSeparator = resolved.LastIndexOf('.');
        return lastMemberSeparator >= 0 && lastMemberSeparator < resolved.Length - 1
            ? resolved[(lastMemberSeparator + 1)..]
            : resolved;
    }

    private static T? ConvertValue<T>(object? value)
    {
        if (value is null)
            return default;

        if (value is T typed)
            return typed;

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (value is JsonElement json)
            return json.Deserialize<T>();

        if (targetType.IsEnum)
            return (T)Enum.Parse(targetType, value.ToString()!, ignoreCase: true);

        return (T?)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private sealed class SimpleMemoryRegister : IMemoryRegister
    {
        public IDictionary<string, IMemoryBlock> Blocks { get; } = new Dictionary<string, IMemoryBlock>(StringComparer.Ordinal);
    }

    private sealed class SimpleMemoryBlock(object? value = null, object? metadata = null) : IMemoryBlock
    {
        public object? Value { get; set; } = value;
        public object? Metadata { get; set; } = metadata;
    }

    private class SimpleMemoryBlockReference(string id) : IMemoryBlockReference
    {
        public string Id { get; set; } = id;

        public virtual IMemoryBlock Declare() => new SimpleMemoryBlock();

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) =>
            ConvertValue<T>(GetValue(memoryRegister));

        public object? Get(IExpressionExecutionContext context) => context.Get(this);

        public T? Get<T>(IExpressionExecutionContext context) => context.Get<T>(this);

        private object? GetValue(IMemoryRegister memoryRegister)
        {
            if (!memoryRegister.Blocks.TryGetValue(Id, out var block))
                block = memoryRegister.Declare(this);

            return block.Value;
        }
    }

    private sealed class SimpleVariable(string name, object? defaultValue = null) : SimpleMemoryBlockReference(name), IVariable
    {
        public string Name { get; set; } = name;
        public object? DefaultValue { get; set; } = defaultValue;
        public Type? StorageDriverType { get; set; }

        public override IMemoryBlock Declare() => new SimpleMemoryBlock(DefaultValue);
    }
}
