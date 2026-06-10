using System.Collections.ObjectModel;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeInputBindingResolutionContext
{
    public RuntimeInputBindingResolutionContext(
        string workflowExecutionId,
        string activityExecutionId,
        IReadOnlyDictionary<string, DurableValueState> durableValuesByValueId,
        IRuntimeActivityOutputRegister activityOutputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentNullException.ThrowIfNull(durableValuesByValueId);
        ArgumentNullException.ThrowIfNull(activityOutputs);

        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        DurableValuesByValueId = new ReadOnlyDictionary<string, DurableValueState>(durableValuesByValueId.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        ActivityOutputs = activityOutputs;
    }

    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }
    public IReadOnlyDictionary<string, DurableValueState> DurableValuesByValueId { get; }
    public IRuntimeActivityOutputRegister ActivityOutputs { get; }
}
