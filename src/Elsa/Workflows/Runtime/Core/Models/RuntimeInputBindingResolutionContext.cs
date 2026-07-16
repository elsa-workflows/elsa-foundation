using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeInputBindingResolutionContext
{
    public RuntimeInputBindingResolutionContext(
        string workflowExecutionId,
        string activityExecutionId,
        IServiceProvider? serviceProvider = null,
        ActivityExecutionState? consumerInvocation = null,
        IReadOnlyCollection<ActivityExecutionState>? runtimeView = null,
        WorkflowExecutable? executable = null,
        IReadOnlyDictionary<string, ValueEnvelope>? workflowInputEnvelopes = null,
        IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>? variableEnvelopes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);

        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        ServiceProvider = serviceProvider;
        ConsumerInvocation = consumerInvocation;
        RuntimeView = runtimeView?.ToArray() ?? [];
        Executable = executable;
        WorkflowInputEnvelopes = SnapshotEnvelopes(workflowInputEnvelopes);
        VariableEnvelopes = SnapshotEnvelopes(variableEnvelopes);
    }

    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }

    /// <summary>
    /// The request-scoped service provider used to evaluate <see cref="RuntimeInputBindingSource.Expression"/>
    /// bindings (e.g. JavaScript/Liquid). Null when only value-carrying bindings are expected, such as the
    /// literal-only resume path.
    /// </summary>
    public IServiceProvider? ServiceProvider { get; }

    public ActivityExecutionState? ConsumerInvocation { get; }
    public IReadOnlyCollection<ActivityExecutionState> RuntimeView { get; }
    public WorkflowExecutable? Executable { get; }
    public IReadOnlyDictionary<string, ValueEnvelope> WorkflowInputEnvelopes { get; }
    public IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope> VariableEnvelopes { get; }

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
