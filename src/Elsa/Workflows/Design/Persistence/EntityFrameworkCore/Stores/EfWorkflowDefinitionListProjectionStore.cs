using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowDefinitionListProjectionStore(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access) : IWorkflowDefinitionListProjectionStore
{
    public async Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        var distinct = ids.Distinct(StringComparer.Ordinal).ToArray(); if (distinct.Length == 0) return [];
        if (distinct.Length > 200) throw new ArgumentException("Workflow definition projection batches are limited to 200 ids.", nameof(ids));
        var drafts = await EfDesignSupport.InScope(db.Drafts.AsNoTracking(), access, x => x.TenantId).Where(x => distinct.Contains(x.WorkflowDefinitionId)).OrderByDescending(x => x.LastModifiedAt).ThenByDescending(x => x.Id).Take(201).ToListAsync(cancellationToken);
        var versions = await EfDesignSupport.InScope(db.Versions.AsNoTracking(), access, x => x.TenantId).Where(x => distinct.Contains(x.DefinitionId)).OrderByDescending(x => x.SemVerSortKey).ThenByDescending(x => x.Id).Take(201).ToListAsync(cancellationToken);
        if (drafts.Count > 200 || versions.Count > 200) throw new InvalidOperationException("Workflow definition projection exceeded the bounded result limit of 200 rows.");
        return distinct.Select(id => { var draft = drafts.FirstOrDefault(x => x.WorkflowDefinitionId == id); var rows = versions.Where(x => x.DefinitionId == id).ToArray(); var latest = rows.FirstOrDefault(); return new WorkflowDefinitionListProjection(id, draft?.Id, latest?.Id, latest?.Version, rows.Length); }).ToArray();
    }
}
