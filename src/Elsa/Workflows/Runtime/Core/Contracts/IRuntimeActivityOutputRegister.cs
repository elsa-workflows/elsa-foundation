using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeActivityOutputRegister
{
    void Set(ActiveActivityOutput output);
    bool TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output);
    IReadOnlyCollection<ActiveActivityOutput> GetActivityOutputs(string workflowExecutionId, string activityExecutionId);
    void ClearActivityOutputs(string workflowExecutionId, string activityExecutionId);
}
