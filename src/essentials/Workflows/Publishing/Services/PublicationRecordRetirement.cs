using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Retires a publication record from the status it is actually in, shared by everything that takes a publication out of
/// service (<see cref="PublicationActivator"/> on replacement, the unpublish handler on withdrawal).
/// </summary>
public static class PublicationRecordRetirement
{
    /// <summary>
    /// Retires a publication the slot no longer names. A candidate is retired too, with an activation time: a process that
    /// stopped after its slot transition left it one, and it served once completion switched it on. A lost
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
}
