using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Removes the matching edge from <c>WorkflowDefinitionState.ActivityConnections</c>. The
/// connection identity is the source+target pair (connections carry no separate Id). Publishes
/// <c>OnConnectionRemovedFromDraft</c>.
/// </summary>
public interface IRemoveConnectionFromDraftCommand
{
    Task Execute(string draftId, ActivityConnection connection, CancellationToken cancellationToken = default);
}
