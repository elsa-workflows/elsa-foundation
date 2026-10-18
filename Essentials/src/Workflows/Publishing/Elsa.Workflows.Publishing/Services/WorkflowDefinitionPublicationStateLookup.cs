using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Design-owned publication-state lookup implemented by Publishing so a composed host can tell
/// Draft from Published, Retracted, and Retired without Design taking a Publishing reference.
/// </summary>
/// <remarks>
/// Live and empty slots are the authority for Published and Retracted. The active version is taken
/// from the configured default slot only when its policy is available through a complete batch read.
/// Retired is journal-only, and only when the record store can answer by definition id. Providers
/// without that capability leave no-slot definitions unresolved rather than guessing Draft.
/// </remarks>
public sealed class WorkflowDefinitionPublicationStateLookup(
    IWorkflowActivationAuthority activationAuthority,
    IPublicationRecordStore publicationStore,
    IPublicationPolicyStore? policyStore = null) : IWorkflowDefinitionPublicationStateLookup
{
    public async Task<IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowDefinitionIds);
        var ids = workflowDefinitionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = new Dictionary<string, WorkflowDefinitionPublicationSummary>(ids.Length, StringComparer.Ordinal);
        if (ids.Length == 0)
            return result;

        var supportsBatchPolicyLookup = policyStore?.SupportsBatchDefinitionLookup == true;
        var workflowPolicies = supportsBatchPolicyLookup
            ? await policyStore!.FindByDefinitionIdsAsync(ids, cancellationToken)
            : new Dictionary<string, PublicationPolicy>(StringComparer.Ordinal);
        var hostPolicy = supportsBatchPolicyLookup
            ? await policyStore!.FindAsync(null, cancellationToken)
            : null;
        var defaultSlotNames = ids.ToDictionary(
            id => id,
            id => supportsBatchPolicyLookup
                ? workflowPolicies.GetValueOrDefault(id)?.DefaultSlotName ?? hostPolicy?.DefaultSlotName ?? "default"
                : null,
            StringComparer.Ordinal);

        var slotsByDefinition = (await activationAuthority.ListByDefinitionIdsAsync(ids, cancellationToken))
            .GroupBy(slot => slot.WorkflowDefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, StringComparer.Ordinal);

        var activeSlotsByDefinition = ids.ToDictionary(
            id => id,
            id => slotsByDefinition.TryGetValue(id, out var definitionSlots)
                ? definitionSlots.Where(slot => slot.ActiveActivationId is not null).ToArray()
                : [],
            StringComparer.Ordinal);
        var activePublicationIds = activeSlotsByDefinition.Values
            .SelectMany(slots => slots)
            .Select(slot => slot.ActiveActivationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var activePublications = activePublicationIds.Length == 0
            ? Array.Empty<PublicationRecord>()
            : await publicationStore.FindByIdsAsync(activePublicationIds, cancellationToken);
        var activePublicationsById = activePublications.ToDictionary(
            publication => publication.PublicationId,
            StringComparer.Ordinal);

        var unresolved = new List<string>();
        foreach (var id in ids)
        {
            if (!slotsByDefinition.TryGetValue(id, out _))
            {
                unresolved.Add(id);
                continue;
            }

            var activeSlots = activeSlotsByDefinition[id];
            if (activeSlots.Length == 0)
            {
                result[id] = new(WorkflowDefinitionPublicationState.Retracted);
                continue;
            }

            var selected = SelectActivePublication(id, activeSlots, activePublicationsById, defaultSlotNames[id]);
            result[id] = new(WorkflowDefinitionPublicationState.Published, selected?.WorkflowDefinitionVersionId);
        }

        if (unresolved.Count == 0 || !publicationStore.SupportsDefinitionIdLookup)
            return result;

        var records = await publicationStore.ListByDefinitionIdsAsync(unresolved, cancellationToken);
        var retired = records
            .Where(record => record.Status == PublicationStatus.Retired)
            .Select(record => record.WorkflowDefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var id in unresolved)
            result[id] = new(retired.Contains(id)
                ? WorkflowDefinitionPublicationState.Retired
                : WorkflowDefinitionPublicationState.Draft);

        return result;
    }

    private static PublicationRecord? SelectActivePublication(
        string definitionId,
        IReadOnlyCollection<WorkflowActivationSlot> activeSlots,
        IReadOnlyDictionary<string, PublicationRecord> publicationsById,
        string? defaultSlotName)
    {
        PublicationRecord? GetRecord(WorkflowActivationSlot slot)
        {
            if (slot.ActiveActivationId is not { } activeId ||
                !publicationsById.TryGetValue(activeId, out var publication) ||
                publication.Status != PublicationStatus.Active ||
                !StringComparer.Ordinal.Equals(publication.WorkflowDefinitionId, definitionId) ||
                !StringComparer.Ordinal.Equals(publication.SlotId, slot.SlotId) ||
                !StringComparer.Ordinal.Equals(publication.SlotName, slot.SlotName))
                return null;
            return publication;
        }

        if (defaultSlotName is not null)
        {
            var defaultSlot = activeSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.SlotName, defaultSlotName));
            if (defaultSlot is not null)
                return GetRecord(defaultSlot);
        }

        var activePublications = activeSlots.Select(GetRecord).ToArray();
        if (activePublications.Length == 0 || activePublications.Any(publication => publication is null))
            return null;

        var versions = activePublications.Select(publication => publication!.WorkflowDefinitionVersionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return versions.Length == 1 ? activePublications[0] : null;
    }
}
