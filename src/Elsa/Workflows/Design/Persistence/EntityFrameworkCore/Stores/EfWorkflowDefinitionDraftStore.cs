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
    public async Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) { var row = await EfDesignSupport.ReadAsync("reading workflow definition draft", () => Query().Where(x => x.WorkflowDefinitionId == workflowDefinitionId).OrderByDescending(x => x.LastModifiedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken)); return row is null ? null : EfDesignSupport.MapDraft(serializer, row); }
    public async Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => (await EfDesignSupport.ReadAsync("listing workflow drafts", () => Query().Where(x => x.WorkflowDefinitionId == workflowDefinitionId).OrderByDescending(x => x.LastModifiedAt).ThenByDescending(x => x.Id).ToListAsync(cancellationToken))).Select(x => EfDesignSupport.MapDraft(serializer, x)).ToArray();
    public async Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) { var row = await EfDesignSupport.ReadAsync("reading workflow draft layout", () => Layout(draftId).SingleOrDefaultAsync(cancellationToken)); return row is null ? [] : EfDesignSupport.ReadLayout(db.Entry(row).Property<string>("RecordsJson").CurrentValue); }
    public async Task<IReadOnlyCollection<ActivityPresentationRecord>> FindActivityPresentationByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) { var row = await EfDesignSupport.ReadAsync("reading workflow draft presentation", () => Layout(draftId).SingleOrDefaultAsync(cancellationToken)); return row is null ? [] : EfDesignSupport.ReadPresentation(db.Entry(row).Property<string>("ActivityPresentationJson").CurrentValue); }
    public async Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) { var draft = await FindByIdAsync(draftId, cancellationToken); if (draft is null) return null; var row = await EfDesignSupport.ReadAsync("reading workflow draft layout", () => Layout(draftId).SingleOrDefaultAsync(cancellationToken)); return new DraftWithLayout(draft, row is null ? [] : EfDesignSupport.ReadLayout(db.Entry(row).Property<string>("RecordsJson").CurrentValue), row is null ? [] : EfDesignSupport.ReadPresentation(db.Entry(row).Property<string>("ActivityPresentationJson").CurrentValue)); }
    private IQueryable<WorkflowDefinitionDraftLayout> Layout(string id) => EfDesignSupport.InScope(db.DraftLayouts, access, x => x.TenantId).Where(x => x.WorkflowDefinitionDraftId == id);
}
