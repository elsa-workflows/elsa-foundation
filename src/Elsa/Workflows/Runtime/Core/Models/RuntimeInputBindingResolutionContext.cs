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
        IServiceProvider? serviceProvider = null)
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
}
