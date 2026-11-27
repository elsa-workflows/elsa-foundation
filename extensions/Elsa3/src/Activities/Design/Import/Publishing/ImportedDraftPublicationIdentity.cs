using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa3.Activities.Design.Import.Contracts;

namespace Elsa3.Activities.Design.Import.Publishing;

internal sealed class ImportedDraftPublicationIdentity(IReusableActivityImportOperationStore store, IActivityDefinitionDraftStore drafts) : IActivityDraftPublicationIdentity
{
    public ValueTask<(string VersionId, string Version)?> FindReservationAsync(string draftId, CancellationToken cancellationToken) =>
        draftId.StartsWith("elsa3-activity-draft-", StringComparison.Ordinal) && store is IImportedActivityDraftReservations reservations
            ? reservations.FindReservationAsync(draftId, cancellationToken)
            : ValueTask.FromResult<(string, string)?>(null);

    public async ValueTask<(string DraftId, string Version)?> FindPrecedingReservationAsync(
        ActivityDefinitionDraft draft, string version, string? publishedHeadVersion, CancellationToken cancellationToken)
    {
        if (!draft.DefinitionId.StartsWith("elsa3-activity-definition-", StringComparison.Ordinal) || store is not IImportedActivityDraftReservations reservations)
            return null;
        var activeIds = await drafts.ListActiveIdsByDefinitionAsync(draft.DefinitionId, draft.TenantId, cancellationToken);
        var siblings = activeIds.Where(id => id != draft.Id && id.StartsWith("elsa3-activity-draft-", StringComparison.Ordinal)).ToArray();
        return await reservations.FindPrecedingReservationAsync(draft.TenantId, siblings, version, publishedHeadVersion, cancellationToken);
    }
}
