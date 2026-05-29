using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Appends an input to a placed activity's <c>Inputs</c> bag. Publishes
/// <c>OnActivityInputAddedToDraft</c>. Per-activity input full CRUD per Unit C FR-019 +
/// Joey iteration 2026-05-28 round 1.
/// </summary>
public interface IAddActivityInputToDraftCommand
{
    Task Execute(string draftId, string nodeId, ArgumentState input, CancellationToken cancellationToken = default);
}
