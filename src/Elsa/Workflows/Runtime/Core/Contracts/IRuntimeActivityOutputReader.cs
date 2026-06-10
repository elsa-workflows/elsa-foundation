using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeActivityOutputReader
{
    bool TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output);
    IReadOnlyCollection<ActiveActivityOutput> GetActivityOutputs(string workflowExecutionId, string activityExecutionId);
}
