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
    public async Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) { var row = await EfDesignSupport.ReadAsync("reading workflow draft", () => Query().SingleOrDefaultAsync(x => x.Id == draftId, cancellationToken)); return row is null ? null : EfDesignSupport.MapDraft(serializer, row); }
    public async Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var key = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(workflowDefinitionId));
        var rows = await EfDesignSupport.ReadAsync("reading workflow definition draft", () => Query()
            .Where(x => EF.Property<string>(x, "WorkflowDefinitionIdLookupHash") == key)
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
            .Where(x => EF.Property<string>(x, "WorkflowDefinitionIdLookupHash") == key)
            .ToListAsync(cancellationToken));
        rows = rows.OrderByDescending(x => x.LastModifiedAt).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id, StringComparer.Ordinal).ToList();
        foreach (var candidate in rows)
            EfDesignSupport.EnsureDefinitionIdentity(workflowDefinitionId, candidate.WorkflowDefinitionId, "workflow definition draft lookup");
        return rows.Select(x => EfDesignSupport.MapDraft(serializer, x)).ToArray();
    }
    public async Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) { var row = await EfDesignSupport.ReadAsync("reading workflow draft layout", () => Layout(draftId).SingleOrDefaultAsync(cancellationToken)); return row is null ? [] : EfDesignSupport.ReadLayout(db.Entry(row).Property<string>("RecordsJson").CurrentValue); }
    public async Task<IReadOnlyCollection<ActivityPresentationRecord>> FindActivityPresentationByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) { var row = await EfDesignSupport.ReadAsync("reading workflow draft presentation", () => Layout(draftId).SingleOrDefaultAsync(cancellationToken)); return row is null ? [] : EfDesignSupport.ReadPresentation(db.Entry(row).Property<string>("ActivityPresentationJson").CurrentValue); }
    public async Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        // Draft and layout are one read contract. Keep them in one provider-translatable
        // left-join so a concurrent update cannot produce a mixed snapshot between statements.
        var drafts = Query();
        var layouts = Layouts();
        var result = await EfDesignSupport.ReadAsync("reading workflow draft with layout", () =>
            (from draft in drafts
             join layout in layouts on draft.Id equals layout.WorkflowDefinitionDraftId into matchingLayouts
             from layout in matchingLayouts.DefaultIfEmpty()
             where draft.Id == draftId
             select new
             {
                 Draft = draft,
                 RecordsJson = layout == null ? null : EF.Property<string>(layout, "RecordsJson"),
                 ActivityPresentationJson = layout == null ? null : EF.Property<string>(layout, "ActivityPresentationJson")
             }).SingleOrDefaultAsync(cancellationToken));

        if (result is null)
            return null;

        var draft = EfDesignSupport.MapDraft(serializer, result.Draft);
        return new DraftWithLayout(
            draft,
            EfDesignSupport.ReadLayout(result.RecordsJson),
            EfDesignSupport.ReadPresentation(result.ActivityPresentationJson));
    }
    private IQueryable<WorkflowDefinitionDraftLayout> Layout(string id) => EfDesignSupport.InScope(db.DraftLayouts, access, x => x.TenantId).Where(x => x.WorkflowDefinitionDraftId == id);
    private IQueryable<WorkflowDefinitionDraftLayout> Layouts() => EfDesignSupport.InScope(db.DraftLayouts.AsNoTracking(), access, x => x.TenantId);
}
