namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Removes the matching input from a placed activity. Publishes
/// <c>OnActivityInputRemovedFromDraft</c>.
/// </summary>
public interface IRemoveActivityInputFromDraftCommand
{
    Task Execute(string draftId, string nodeId, string inputReferenceKey, CancellationToken cancellationToken = default);
}
