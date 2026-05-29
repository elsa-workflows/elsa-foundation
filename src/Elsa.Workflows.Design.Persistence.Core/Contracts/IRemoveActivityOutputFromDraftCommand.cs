namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Removes the matching output from a placed activity. Publishes
/// <c>OnActivityOutputRemovedFromDraft</c>.
/// </summary>
public interface IRemoveActivityOutputFromDraftCommand
{
    Task Execute(string draftId, string nodeId, string outputReferenceKey, CancellationToken cancellationToken = default);
}
