using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Updates the matching output on a placed activity. Publishes
/// <c>OnActivityOutputUpdatedInDraft</c>.
/// </summary>
public interface IUpdateActivityOutputInDraftCommand
{
    Task Execute(string draftId, string nodeId, string outputReferenceKey, ArgumentState newValue, CancellationToken cancellationToken = default);
}
