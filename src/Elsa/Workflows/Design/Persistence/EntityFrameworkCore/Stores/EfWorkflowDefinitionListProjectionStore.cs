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
        var folded = distinct.Select(id => EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(id))).ToArray();
        var drafts = new List<WorkflowDefinitionDraft>();
        var versions = new List<WorkflowDefinitionVersion>();
        foreach (var batch in folded.Chunk(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            drafts.AddRange(await EfDesignSupport.ReadAsync("reading workflow draft projections", () => EfDesignSupport.InScope(db.Drafts.AsNoTracking(), access, x => x.TenantId)
                .Where(x => batch.Contains(EF.Property<string>(x, "WorkflowDefinitionIdLookupHash")))
                .ToListAsync(cancellationToken)));
            versions.AddRange(await EfDesignSupport.ReadAsync("reading workflow version projections", () => EfDesignSupport.InScope(db.Versions.AsNoTracking(), access, x => x.TenantId)
                .Where(x => batch.Contains(EF.Property<string>(x, "DefinitionIdLookupHash")))
                .ToListAsync(cancellationToken)));
        }

        var latestDrafts = drafts
            .GroupBy(x => EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(x.WorkflowDefinitionId)), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.LastModifiedAt)
                    .ThenByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Id, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);
        var definitionVersions = versions
            .GroupBy(x => EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(x.DefinitionId)), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal)
                    .ThenByDescending(x => x.Id, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return distinct.Select(id =>
        {
            var key = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(id));
            latestDrafts.TryGetValue(key, out var draft);
            definitionVersions.TryGetValue(key, out var rows);
            var latest = rows?.FirstOrDefault();
            return new WorkflowDefinitionListProjection(id, draft?.Id, latest?.Id, latest?.Version, rows?.Length ?? 0);
        }).ToArray();
    }
}
