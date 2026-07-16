using System.Collections.ObjectModel;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeInputBindingResolutionContext
{
    public RuntimeInputBindingResolutionContext(
        string workflowExecutionId,
        string activityExecutionId,
        IReadOnlyDictionary<string, DurableValueState> durableValuesByValueId,
        IRuntimeActivityOutputReader activityOutputs,
        IServiceProvider? serviceProvider = null,
        IReadOnlyDictionary<string, object?>? workflowVariables = null,
        IReadOnlyDictionary<string, object?>? workflowInputs = null,
        IReadOnlyDictionary<string, object?>? activityOutputValues = null,
        VariableScope? variableScope = null,
        ActivityExecutionState? consumerInvocation = null,
        IReadOnlyCollection<ActivityExecutionState>? runtimeView = null,
        WorkflowExecutable? executable = null,
        IReadOnlyDictionary<string, ValueEnvelope>? workflowInputEnvelopes = null,
        IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>? variableEnvelopes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentNullException.ThrowIfNull(durableValuesByValueId);
        ArgumentNullException.ThrowIfNull(activityOutputs);

        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        DurableValuesByValueId = new ReadOnlyDictionary<string, DurableValueState>(durableValuesByValueId.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        ActivityOutputs = activityOutputs;
        ServiceProvider = serviceProvider;
        WorkflowVariables = Snapshot(workflowVariables);
        WorkflowInputs = Snapshot(workflowInputs);
        ActivityOutputValues = Snapshot(activityOutputValues);
        VariableScope = variableScope;
        ConsumerInvocation = consumerInvocation;
        RuntimeView = runtimeView?.ToArray() ?? [];
        Executable = executable;
        WorkflowInputEnvelopes = SnapshotEnvelopes(workflowInputEnvelopes);
        VariableEnvelopes = SnapshotEnvelopes(variableEnvelopes);
    }

    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }
    public IReadOnlyDictionary<string, DurableValueState> DurableValuesByValueId { get; }
    public IRuntimeActivityOutputReader ActivityOutputs { get; }

    /// <summary>
    /// The request-scoped service provider used to evaluate <see cref="RuntimeInputBindingSource.Expression"/>
    /// bindings (e.g. JavaScript/Liquid). Null when only value-carrying bindings are expected, such as the
    /// literal-only resume path.
    /// </summary>
    public IServiceProvider? ServiceProvider { get; }

    /// <summary>
    /// Current workflow variable values keyed by variable name, exposed to <see cref="RuntimeInputBindingSource.Expression"/>
    /// bindings (e.g. the JavaScript <c>variables.foo</c> accessor). Empty when no variables are in scope.
    /// </summary>
    public IReadOnlyDictionary<string, object?> WorkflowVariables { get; }

    /// <summary>
    /// Workflow input values keyed by input name, exposed to <see cref="RuntimeInputBindingSource.Expression"/>
    /// bindings (e.g. the JavaScript <c>input.foo</c> accessor or <c>getInput("foo")</c>). Empty when none are available.
    /// </summary>
    public IReadOnlyDictionary<string, object?> WorkflowInputs { get; }

    /// <summary>
    /// Prior activity output values keyed by output name, exposed to <see cref="RuntimeInputBindingSource.Expression"/>
    /// bindings (e.g. the JavaScript <c>output.foo</c> accessor or <c>getOutput("foo")</c>). Empty when none are available.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ActivityOutputValues { get; }

    /// <summary>
    /// The innermost visible <see cref="VariableScope"/> for the activity whose inputs are being
    /// materialized (its declaring-container chain up to the workflow scope, ADR 0027). Null when the
    /// activity has no enclosing container scope chain. Threaded into the materialization expression
    /// context so structured <c>Variable</c> references and name-based script helpers resolve and
    /// assign container-scoped variables in production.
    /// </summary>
    public VariableScope? VariableScope { get; }
    public ActivityExecutionState? ConsumerInvocation { get; }
    public IReadOnlyCollection<ActivityExecutionState> RuntimeView { get; }
    public WorkflowExecutable? Executable { get; }
    public IReadOnlyDictionary<string, ValueEnvelope> WorkflowInputEnvelopes { get; }
    public IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope> VariableEnvelopes { get; }

    private static IReadOnlyDictionary<string, object?> Snapshot(IReadOnlyDictionary<string, object?>? values) =>
        new ReadOnlyDictionary<string, object?>(
            (values ?? new Dictionary<string, object?>()).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, ValueEnvelope> SnapshotEnvelopes(
        IReadOnlyDictionary<string, ValueEnvelope>? values) =>
        new ReadOnlyDictionary<string, ValueEnvelope>(
            (values ?? new Dictionary<string, ValueEnvelope>()).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    private static IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope> SnapshotEnvelopes(
        IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>? values) =>
        new ReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>(
            (values ?? new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>()).ToDictionary());
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
