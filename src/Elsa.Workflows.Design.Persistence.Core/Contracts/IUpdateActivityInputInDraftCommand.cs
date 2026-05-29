using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Updates the matching input on a placed activity. Publishes
/// <c>OnActivityInputUpdatedInDraft</c>.
/// </summary>
public interface IUpdateActivityInputInDraftCommand
{
    Task Execute(string draftId, string nodeId, string inputReferenceKey, ArgumentState newValue, CancellationToken cancellationToken = default);
}
