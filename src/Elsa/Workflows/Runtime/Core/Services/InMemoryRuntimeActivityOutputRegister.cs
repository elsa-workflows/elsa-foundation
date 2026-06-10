using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryRuntimeActivityOutputRegister : IRuntimeActivityOutputRegister
{
    private readonly Dictionary<ActiveActivityOutputKey, ActiveActivityOutput> _outputs = new();

    public void Set(ActiveActivityOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputs[output.Key] = output;
    }

    public bool TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _outputs.TryGetValue(key, out output!);
    }

    public IReadOnlyCollection<ActiveActivityOutput> GetActivityOutputs(string workflowExecutionId, string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);

        return _outputs
            .Where(item => item.Key.WorkflowExecutionId == workflowExecutionId && item.Key.ActivityExecutionId == activityExecutionId)
            .Select(item => item.Value)
            .ToArray();
    }

    public void ClearActivityOutputs(string workflowExecutionId, string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);

        foreach (var key in _outputs.Keys
                     .Where(key => key.WorkflowExecutionId == workflowExecutionId && key.ActivityExecutionId == activityExecutionId)
                     .ToArray())
        {
            _outputs.Remove(key);
        }
    }
}
