using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeActivityOutputReader
{
    bool TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output);
    IReadOnlyCollection<ActiveActivityOutput> GetActivityOutputs(string workflowExecutionId, string activityExecutionId);
}

public interface IRuntimeActivityOutputRegister : IRuntimeActivityOutputReader
{
    void Set(ActiveActivityOutput output);
    void ClearActivityOutputs(string workflowExecutionId, string activityExecutionId);
}
