using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Design-owned publication-state lookup implemented by Publishing so a composed host can tell
/// Draft from Published, Retracted, and Retired without Design taking a Publishing reference.
/// </summary>
/// <remarks>
/// Live and empty slots are the authority for Published and Retracted. Retired is journal-only,
/// and only when the record store can answer by definition id; the EF ledger's default is empty
/// because it has no definition-keyed index.
/// </remarks>
public sealed class WorkflowDefinitionPublicationStateLookup(
    IWorkflowActivationAuthority activationAuthority,
    IPublicationRecordStore publicationStore) : IWorkflowDefinitionPublicationStateLookup
{
    public async Task<IReadOnlyDictionary<string, WorkflowDefinitionPublicationState>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowDefinitionIds);
        var ids = workflowDefinitionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = new Dictionary<string, WorkflowDefinitionPublicationState>(ids.Length, StringComparer.Ordinal);
        if (ids.Length == 0)
            return result;

        var unresolved = new List<string>();
        foreach (var id in ids)
        {
            var slots = await activationAuthority.ListByDefinitionAsync(id, cancellationToken);
            if (slots.Any(slot => slot.ActiveActivationId is not null))
                result[id] = WorkflowDefinitionPublicationState.Published;
            else if (slots.Count > 0)
                result[id] = WorkflowDefinitionPublicationState.Retracted;
            else
                unresolved.Add(id);
        }

        if (unresolved.Count == 0)
            return result;

        var records = await publicationStore.ListByDefinitionIdsAsync(unresolved, cancellationToken);
        var retired = records
            .Where(record => record.Status == PublicationStatus.Retired)
            .Select(record => record.WorkflowDefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var id in unresolved)
            result[id] = retired.Contains(id)
                ? WorkflowDefinitionPublicationState.Retired
                : WorkflowDefinitionPublicationState.Draft;

        return result;
    }
}
