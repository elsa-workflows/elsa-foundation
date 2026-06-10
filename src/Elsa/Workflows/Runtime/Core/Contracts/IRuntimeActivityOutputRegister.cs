using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeActivityOutputRegister : IRuntimeActivityOutputReader
{
    void Set(ActiveActivityOutput output);
    void ClearActivityOutputs(string workflowExecutionId, string activityExecutionId);
}
