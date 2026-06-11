using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeActivityInputMaterializer
{
    IReadOnlyList<RuntimeMaterializedActivityInput> MaterializeInputs(ExecutableNode node);
}
