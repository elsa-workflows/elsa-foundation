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
    : IRuntimeActivityExecutionContext, IExpressionExecutionContext, IScopedVariableProvider
{
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
    public string GetCorrelationId() => string.Empty;
    public string GetWorkflowDefinitionId() => string.Empty;
    public string GetWorkflowDefinitionVersionId() => string.Empty;
    public int GetWorkflowDefinitionVersion() => 0;
    public string GetWorkflowInstanceId() => string.Empty;

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
