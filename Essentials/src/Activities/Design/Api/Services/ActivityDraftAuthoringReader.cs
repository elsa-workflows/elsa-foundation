using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>Applies the activity author's read, tenant and provider-payload boundary before reading graph metadata.</summary>
internal sealed class ActivityDraftAuthoringReader(
    IActivityDefinitionDraftStore drafts,
    IActivityAuthoringCatalog catalog,
    IActivityAuthoringContextAsync context) : IActivityDraftAuthoringReader
{
    public async ValueTask<ActivityDraftAuthoringSource?> FindAsync(string draftId, CancellationToken cancellationToken = default)
    {
        if (!await catalog.CanReadAsync(cancellationToken))
            return null;
        var draft = await drafts.FindAsync(draftId, cancellationToken);
        if (draft is null || (draft.TenantId is not null && !string.Equals(draft.TenantId, context.TenantId, StringComparison.Ordinal)) ||
            !await context.CanReadProviderPayloadAsync(draft.State.Provider.ProviderKey, cancellationToken))
            return null;
        return new(draft.Id, draft.Revision, draft.State, await context.GetAuthorizationProfileAsync(cancellationToken));
    }
}
