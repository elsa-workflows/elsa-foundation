using System.Collections.ObjectModel;
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
        IReadOnlyDictionary<string, object?>? activityOutputValues = null)
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

    private static IReadOnlyDictionary<string, object?> Snapshot(IReadOnlyDictionary<string, object?>? values) =>
        new ReadOnlyDictionary<string, object?>(
            (values ?? new Dictionary<string, object?>()).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
}
