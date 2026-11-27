using Elsa.Workflows.Publishing.Api.Services;
using Elsa3.Activities.Design.Import.Contracts;

namespace Elsa3.Activities.Design.Import.Publishing;

internal sealed class ImportedDraftPublicationIdentity(IReusableActivityImportOperationStore store) : IActivityDraftPublicationIdentity
{
    public ValueTask<(string VersionId, string Version)?> FindReservationAsync(string draftId, CancellationToken cancellationToken) =>
        draftId.StartsWith("elsa3-activity-draft-", StringComparison.Ordinal) && store is IImportedActivityDraftReservations reservations
            ? reservations.FindReservationAsync(draftId, cancellationToken)
            : ValueTask.FromResult<(string, string)?>(null);
}
