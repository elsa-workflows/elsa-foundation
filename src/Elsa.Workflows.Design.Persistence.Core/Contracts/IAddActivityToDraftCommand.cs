using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Appends a placed activity to <c>WorkflowDefinitionState.Activities</c> on the Draft.
/// Publishes <c>OnActivityAddedToDraft</c>. Per Unit C FR-019 activities-graph bullet.
/// </summary>
public interface IAddActivityToDraftCommand
{
    Task Execute(string draftId, ActivityNode activity, CancellationToken cancellationToken = default);
}
