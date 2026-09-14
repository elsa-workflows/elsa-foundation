using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowDefinitionDraftStore(WorkflowsDesignDbContext db, IPayloadSerializer serializer, IPersistenceAccessContextAccessor access) : IWorkflowDefinitionDraftStore
{
    private IQueryable<WorkflowDefinitionDraft> Query() => EfDesignSupport.InScope(db.Drafts.AsNoTracking(), access, x => x.TenantId);
    public async Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        var idHash = EfDesignSupport.LookupHash(draftId);
        var row = await EfDesignSupport.ReadAsync("reading workflow draft", () => Query().SingleOrDefaultAsync(x => x.IdLookupHash == idHash, cancellationToken));
        if (row is null)
            return null;
        EfDesignSupport.EnsureExactIdentity(draftId, row.Id, "workflow draft lookup");
        return EfDesignSupport.MapDraft(serializer, row);
    }
    public async Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var key = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(workflowDefinitionId));
        var rows = await EfDesignSupport.ReadAsync("reading workflow definition draft", () => Query()
            .Where(x => x.WorkflowDefinitionIdLookupHash == key)
            .ToListAsync(cancellationToken));
        rows = rows.OrderByDescending(x => x.LastModifiedAt).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id, StringComparer.Ordinal).ToList();
        foreach (var candidate in rows)
            EfDesignSupport.EnsureDefinitionIdentity(workflowDefinitionId, candidate.WorkflowDefinitionId, "workflow definition draft lookup");
        return rows.FirstOrDefault() is { } row ? EfDesignSupport.MapDraft(serializer, row) : null;
    }

    public async Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var key = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(workflowDefinitionId));
        var rows = await EfDesignSupport.ReadAsync("listing workflow drafts", () => Query()
            .Where(x => x.WorkflowDefinitionIdLookupHash == key)
            .ToListAsync(cancellationToken));
        rows = rows.OrderByDescending(x => x.LastModifiedAt).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id, StringComparer.Ordinal).ToList();
        foreach (var candidate in rows)
            EfDesignSupport.EnsureDefinitionIdentity(workflowDefinitionId, candidate.WorkflowDefinitionId, "workflow definition draft lookup");
        return rows.Select(x => EfDesignSupport.MapDraft(serializer, x)).ToArray();
    }
    public async Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        var row = await EfDesignSupport.ReadAsync("reading workflow draft layout", () => Layout(draftId).SingleOrDefaultAsync(cancellationToken));
        if (row is null)
            return [];
        EfDesignSupport.EnsureExactIdentity(draftId, row.WorkflowDefinitionDraftId, "workflow draft layout lookup");
        return EfDesignSupport.ReadLayout(row.RecordsJson);
    }

    public async Task<IReadOnlyCollection<ActivityPresentationRecord>> FindActivityPresentationByDraftIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        var row = await EfDesignSupport.ReadAsync("reading workflow draft presentation", () => Layout(draftId).SingleOrDefaultAsync(cancellationToken));
        if (row is null)
            return [];
        EfDesignSupport.EnsureExactIdentity(draftId, row.WorkflowDefinitionDraftId, "workflow draft presentation lookup");
        return EfDesignSupport.ReadPresentation(row.ActivityPresentationJson);
    }
    public async Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        // Draft and layout are one read contract. Keep them in one provider-translatable
        // left-join so a concurrent update cannot produce a mixed snapshot between statements.
        var drafts = Query();
        var layouts = Layouts();
        var result = await EfDesignSupport.ReadAsync("reading workflow draft with layout", () =>
            (from draft in drafts
             join layout in layouts on draft.IdLookupHash equals layout.WorkflowDefinitionDraftIdLookupHash into matchingLayouts
             from layout in matchingLayouts.DefaultIfEmpty()
             where draft.IdLookupHash == EfDesignSupport.LookupHash(draftId)
             select new
             {
                 Draft = draft,
                 LayoutDraftId = layout == null ? null : layout.WorkflowDefinitionDraftId,
                 RecordsJson = layout == null ? null : layout.RecordsJson,
                 ActivityPresentationJson = layout == null ? null : layout.ActivityPresentationJson
             }).SingleOrDefaultAsync(cancellationToken));

        if (result is null)
            return null;

        EfDesignSupport.EnsureExactIdentity(draftId, result.Draft.Id, "workflow draft with layout lookup");
        if (result.LayoutDraftId is not null)
            EfDesignSupport.EnsureExactIdentity(draftId, result.LayoutDraftId, "workflow draft layout lookup");

        var draft = EfDesignSupport.MapDraft(serializer, result.Draft);
        return new DraftWithLayout(
            draft,
            EfDesignSupport.ReadLayout(result.RecordsJson),
            EfDesignSupport.ReadPresentation(result.ActivityPresentationJson));
    }
    private IQueryable<WorkflowDefinitionDraftLayout> Layout(string id) => EfDesignSupport.InScope(db.DraftLayouts, access, x => x.TenantId).Where(x => x.WorkflowDefinitionDraftIdLookupHash == EfDesignSupport.LookupHash(id));
    private IQueryable<WorkflowDefinitionDraftLayout> Layouts() => EfDesignSupport.InScope(db.DraftLayouts.AsNoTracking(), access, x => x.TenantId);
}
