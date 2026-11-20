using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeInputBindingResolutionContext
{
    public RuntimeInputBindingResolutionContext(
        string workflowExecutionId,
        string activityExecutionId,
        ActivityExecutionState? consumerInvocation = null,
        IReadOnlyCollection<ActivityExecutionState>? runtimeView = null,
        WorkflowExecutable? executable = null,
        IReadOnlyDictionary<string, ValueEnvelope>? workflowInputEnvelopes = null,
        IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>? variableEnvelopes = null,
        IReadOnlyList<RuntimeReadableVariable>? visibleVariables = null,
        string? correlationId = null,
        RuntimeVariableReadRecorder? variableReadRecorder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);

        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        ConsumerInvocation = consumerInvocation;
        RuntimeView = runtimeView?.ToArray() ?? [];
        Executable = executable;
        WorkflowInputEnvelopes = SnapshotEnvelopes(workflowInputEnvelopes);
        VariableEnvelopes = SnapshotEnvelopes(variableEnvelopes);
        VisibleVariables = visibleVariables?.ToArray() ?? [];
        CorrelationId = correlationId;
        VariableReadRecorder = variableReadRecorder;
    }

    /// <summary>
    /// Where the instrumented variable reads of this resolution are recorded (spec 1221 input read set). Null when the
    /// producer does not track reads.
    /// </summary>
    public RuntimeVariableReadRecorder? VariableReadRecorder { get; }

    /// <summary>Where input expressions report warnings during this materialization (spec 1228); <see langword="null"/> drops them.</summary>
    public Elsa.Expressions.Core.Models.ExecutionWarningCollector? Warnings { get; init; }

    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }

    public ActivityExecutionState? ConsumerInvocation { get; }
    public IReadOnlyCollection<ActivityExecutionState> RuntimeView { get; }
    public WorkflowExecutable? Executable { get; }
    public IReadOnlyDictionary<string, ValueEnvelope> WorkflowInputEnvelopes { get; }
    public IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope> VariableEnvelopes { get; }

    /// <summary>
    /// The variables lexically visible to the consuming activity, nearest scope first and each name once (the innermost
    /// declaration wins), with where each is declared. The run-time fallback of a computed or opaque script read
    /// resolves names over them (spec 1222 FR-008); only an expression that carries the fallback marker uses them.
    /// Distinct from <see cref="VariableEnvelopes"/>, which is keyed by declaring-scope address and drives pinned
    /// <c>Variable</c> parameters. Empty when the producer supplies no projection.
    /// </summary>
    public IReadOnlyList<RuntimeReadableVariable> VisibleVariables { get; }

    /// <summary>
    /// The instance's correlation ID as committed in its identity durable values, read by a
    /// <c>WorkflowInstance</c> expression parameter binding. Null when the instance has none.
    /// </summary>
    public string? CorrelationId { get; }

    private static IReadOnlyDictionary<string, ValueEnvelope> SnapshotEnvelopes(
        IReadOnlyDictionary<string, ValueEnvelope>? values) =>
        new ReadOnlyDictionary<string, ValueEnvelope>(
            (values ?? new Dictionary<string, ValueEnvelope>()).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    private static IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope> SnapshotEnvelopes(
        IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>? values) =>
        new ReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>(
            (values ?? new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>()).ToDictionary());
}

/// <summary>One visible variable a script's computed read can reach: its author-facing name, declaring scope, key and committed envelope.</summary>
public sealed record RuntimeReadableVariable
{
    public RuntimeReadableVariable(string name, string scopeId, string variableKey, ValueEnvelope envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        ArgumentNullException.ThrowIfNull(envelope);
        Name = name;
        ScopeId = scopeId;
        VariableKey = variableKey;
        Envelope = envelope;
    }

    public string Name { get; }
    public string ScopeId { get; }
    public string VariableKey { get; }
    public ValueEnvelope Envelope { get; }
}

/// <summary>
/// Stable address of a variable value inside its declaring lexical scope. This is a resolution key,
/// not a general-purpose mutable value address.
/// </summary>
public readonly record struct RuntimeVariableValueAddress
{
    public RuntimeVariableValueAddress(string declaringScopeId, string variableKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringScopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);

        DeclaringScopeId = declaringScopeId;
        VariableKey = variableKey;
    }

    public string DeclaringScopeId { get; }
    public string VariableKey { get; }
}
