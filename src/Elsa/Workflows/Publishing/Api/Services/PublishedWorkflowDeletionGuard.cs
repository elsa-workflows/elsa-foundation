using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Refuses permanent deletion of a workflow definition while it is still live in the publishing vertical:
/// a publication slot that holds an active publication, or a live Published-scope executable source reference.
/// Hard-deleting the design rows in that state strands the runtime executable and its Published reference
/// against a definition that no longer exists, so the definition must be unpublished first. Both checks run
/// because the slot authority and the source reference can desync — either one alone keeps the runtime live.
/// </summary>
public sealed class PublishedWorkflowDeletionGuard(
    IPublicationSlotStore slotStore,
    IWorkflowExecutableSourceReferenceReader sourceReferenceReader,
    TimeProvider timeProvider) : IWorkflowDefinitionPermanentDeletionGuard
{
    public async Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        var slots = await slotStore.ListByDefinitionAsync(definitionId, cancellationToken);
        var activeSlotNames = slots
            .Where(slot => slot.ActivePublicationId is not null)
            .Select(slot => slot.SlotName)
            .ToArray();
        if (activeSlotNames.Length > 0)
            throw new InvalidOperationException(
                $"Workflow definition '{definitionId}' still has an active publication in slot(s) " +
                $"{string.Join(", ", activeSlotNames.Select(name => $"'{name}'"))}; unpublish it before deleting it permanently.");

        var liveReferences = await sourceReferenceReader.ListAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (liveReferences.Any(reference => StringComparer.Ordinal.Equals(reference.DefinitionId, definitionId)))
            throw new InvalidOperationException(
                $"Workflow definition '{definitionId}' still has a live published executable source reference; " +
                "unpublish it before deleting it permanently.");
    }
}
