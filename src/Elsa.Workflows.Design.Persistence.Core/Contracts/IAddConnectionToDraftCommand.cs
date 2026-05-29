using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Appends an edge to <c>WorkflowDefinitionState.ActivityConnections</c>. Publishes
/// <c>OnConnectionAddedToDraft</c>.
/// </summary>
public interface IAddConnectionToDraftCommand
{
    Task Execute(string draftId, ActivityConnection connection, CancellationToken cancellationToken = default);
}
