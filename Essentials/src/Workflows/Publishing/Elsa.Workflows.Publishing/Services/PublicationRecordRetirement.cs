using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Executables;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Retires a publication record from the status it is actually in, shared by everything that takes a publication out of
/// service (<see cref="PublicationActivator"/> on replacement, the unpublish handler on withdrawal).
/// </summary>
public static class PublicationRecordRetirement
{
    /// <summary>
    /// Retires a publication the slot no longer names. A candidate is retired too, with an activation time: a process that
    /// stopped after the runtime's switch left it one, and it served from that switch on. A lost
    /// compare-and-swap is re-read once, because a concurrent completion may just have made the candidate active. A
    /// publication already retired or failed is left as it is.
    /// </summary>
    public static async ValueTask RetireAsync(
        IPublicationRecordStore publicationStore,
        PublicationRecord? publication,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publicationStore);
        for (var attempt = 0; publication is { Status: PublicationStatus.Candidate or PublicationStatus.Active }; attempt++)
        {
            if (attempt == 2)
                throw new InvalidOperationException(
                    $"Publication '{publication.PublicationId}' did not transition from '{publication.Status}' to 'Retired'.");
            var retired = publication with
            {
                Status = PublicationStatus.Retired,
                ActivatedAt = publication.ActivatedAt ?? now,
                RetiredAt = now
            };
            if (await publicationStore.TryTransitionAsync(retired, publication.Status, cancellationToken))
                return;
            publication = await publicationStore.FindAsync(publication.PublicationId, cancellationToken);
        }
    }

    /// <summary>
    /// Retires every active publication of a slot other than <paramref name="exceptPublicationId"/> whose activation the
    /// runtime has recorded as replaced: its source reference is retired or gone. One whose reference is live is left
    /// alone, because nothing then says it stopped serving (it may be a publication that just won the slot), and so is
    /// one whose reference a failed activation's compensation retired: that activation's own activator records it as
    /// failed.
    /// </summary>
    /// <returns>The publications this call retired.</returns>
    public static async ValueTask<IReadOnlyList<string>> RetireReplacedAsync(
        IPublicationRecordStore publicationStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        string slotId,
        string exceptPublicationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publicationStore);
        ArgumentNullException.ThrowIfNull(sourceReferenceStore);
        var retired = new List<string>();
        foreach (var other in (await publicationStore.ListBySlotAsync(slotId, cancellationToken))
                     .Where(other => other.Status == PublicationStatus.Active && !StringComparer.Ordinal.Equals(other.PublicationId, exceptPublicationId)))
        {
            var reference = other.SourceReferenceId is { } sourceReferenceId
                ? await sourceReferenceStore.FindAsync(sourceReferenceId, cancellationToken)
                : null;
            if (reference is { DeletedAt: null } ||
                StringComparer.Ordinal.Equals(reference?.DeletedReason, WorkflowActivationCoordinator.FailedRetireReason))
                continue;
            await RetireAsync(publicationStore, other, now, cancellationToken);
            retired.Add(other.PublicationId);
        }

        return retired;
    }
}
