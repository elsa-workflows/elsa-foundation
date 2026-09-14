using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowDefinitionListProjectionStore(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access) : IWorkflowDefinitionListProjectionStore
{
    public async Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        var distinct = ids.Distinct(StringComparer.Ordinal).ToArray(); if (distinct.Length == 0) return [];
        var drafts = new List<WorkflowDefinitionDraft>();
        var versions = new List<WorkflowDefinitionVersion>();
        foreach (var batch in distinct.Chunk(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            drafts.AddRange(await EfDesignSupport.InScope(db.Drafts.AsNoTracking(), access, x => x.TenantId)
                .Where(x => batch.Contains(x.WorkflowDefinitionId))
                .ToListAsync(cancellationToken));
            versions.AddRange(await EfDesignSupport.InScope(db.Versions.AsNoTracking(), access, x => x.TenantId)
                .Where(x => batch.Contains(x.DefinitionId))
                .ToListAsync(cancellationToken));
        }

        var latestDrafts = drafts
            .GroupBy(x => x.WorkflowDefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.LastModifiedAt)
                    .ThenByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Id, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);
        var definitionVersions = versions
            .GroupBy(x => x.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal)
                    .ThenByDescending(x => x.Id, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return distinct.Select(id =>
        {
            latestDrafts.TryGetValue(id, out var draft);
            definitionVersions.TryGetValue(id, out var rows);
            var latest = rows?.FirstOrDefault();
            return new WorkflowDefinitionListProjection(id, draft?.Id, latest?.Id, latest?.Version, rows?.Length ?? 0);
        }).ToArray();
    }
}
