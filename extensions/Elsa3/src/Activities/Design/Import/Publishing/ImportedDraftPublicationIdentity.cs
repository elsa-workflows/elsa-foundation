using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Versioning;
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
        ActivityDefinitionDraft draft, string version, CancellationToken cancellationToken)
    {
        if (!draft.DefinitionId.StartsWith("elsa3-activity-definition-", StringComparison.Ordinal) ||
            store is not IImportedActivityDraftReservations || !SemVer.TryParse(version, out var candidate))
            return null;

        (string DraftId, string Version)? earliest = null;
        var boundary = candidate;
        foreach (var sibling in await drafts.ListByDefinitionAsync(draft.DefinitionId, cancellationToken))
        {
            if (sibling.Id == draft.Id || sibling.TenantId != draft.TenantId || sibling.Status != ActivityDefinitionDraftStatus.Active)
                continue;
            var reservation = await FindReservationAsync(sibling.Id, cancellationToken);
            if (reservation is { } reserved && SemVer.TryParse(reserved.Version, out var pending) && pending <= boundary)
            {
                earliest = (sibling.Id, reserved.Version);
                boundary = pending;
            }
        }
        return earliest;
    }
}
