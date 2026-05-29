using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Appends an output to a placed activity's <c>Outputs</c> bag. Publishes
/// <c>OnActivityOutputAddedToDraft</c>. Per-activity output full CRUD per Unit C FR-019 +
/// Joey iteration 2026-05-28 round 1.
/// </summary>
public interface IAddActivityOutputToDraftCommand
{
    Task Execute(string draftId, string nodeId, ArgumentState output, CancellationToken cancellationToken = default);
}
