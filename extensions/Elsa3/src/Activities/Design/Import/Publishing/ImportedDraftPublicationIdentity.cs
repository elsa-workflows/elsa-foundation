using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa3.Activities.Design.Import.Contracts;

namespace Elsa3.Activities.Design.Import.Publishing;

internal sealed class ImportedDraftPublicationIdentity(IReusableActivityImportOperationStore store) : IActivityDraftPublicationIdentity
{
    public ValueTask<(string VersionId, string Version)?> FindReservationAsync(string draftId, CancellationToken cancellationToken) =>
        draftId.StartsWith("elsa3-activity-draft-", StringComparison.Ordinal) && store is IImportedActivityDraftReservations reservations
            ? reservations.FindReservationAsync(draftId, cancellationToken)
            : ValueTask.FromResult<(string, string)?>(null);

    public ValueTask<(string DraftId, string Version)?> FindPrecedingReservationAsync(
        ActivityDefinitionDraft draft, string version, string? publishedHeadVersion, CancellationToken cancellationToken) =>
        draft.DefinitionId.StartsWith("elsa3-activity-definition-", StringComparison.Ordinal) && store is IImportedActivityDraftReservations reservations
            ? reservations.FindPrecedingReservationAsync(draft.TenantId, draft.DefinitionId, draft.Id, version, publishedHeadVersion, cancellationToken)
            : ValueTask.FromResult<(string, string)?>(null);
}
