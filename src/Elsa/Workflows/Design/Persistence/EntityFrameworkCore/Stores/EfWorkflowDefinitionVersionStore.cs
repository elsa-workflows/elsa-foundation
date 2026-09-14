using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowDefinitionVersionStore(WorkflowsDesignDbContext db, IPayloadSerializer serializer, IWorkflowDefinitionStore definitions, IPersistenceAccessContextAccessor access) : IWorkflowDefinitionVersionStore
{
    private IQueryable<WorkflowDefinitionVersion> Query() => EfDesignSupport.InScope(db.Versions.AsNoTracking(), access, x => x.TenantId);
    public async Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => await FindByIdAsync(versionId, cancellationToken) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionVersion), versionId);
    public async Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) { var row = await EfDesignSupport.ReadAsync("reading workflow definition version", () => Query().SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)); return row is null ? null : EfDesignSupport.MapVersion(serializer, row); }
    public async Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) { var row = await GetAsync(versionId, cancellationToken); row.Definition = await definitions.GetAsync(row.DefinitionId, cancellationToken); return row; }
    public async Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        var key = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(definitionId));
        var rows = await EfDesignSupport.ReadAsync("reading latest workflow definition version", () => Query()
            .Where(x => x.DefinitionIdLookupHash == key)
            .OrderByDescending(x => x.SemVerSortKey)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken));
        foreach (var candidate in rows)
            EfDesignSupport.EnsureDefinitionIdentity(definitionId, candidate.DefinitionId, "latest workflow definition version lookup");
        return rows.FirstOrDefault() is { } row ? EfDesignSupport.MapVersion(serializer, row) : null;
    }

    public async Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        var key = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(definitionId));
        var rows = await EfDesignSupport.ReadAsync("listing workflow definition versions", () => Query()
            .Where(x => x.DefinitionIdLookupHash == key)
            .OrderBy(x => x.SemVerSortKey)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken));
        foreach (var candidate in rows)
            EfDesignSupport.EnsureDefinitionIdentity(definitionId, candidate.DefinitionId, "workflow definition version lookup");
        return rows.Select(x => EfDesignSupport.MapVersion(serializer, x)).ToArray();
    }

    public async Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
    {
        var key = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(definitionId));
        var rows = await EfDesignSupport.ReadAsync("checking workflow definition version", () => Query()
            .Where(x => x.DefinitionIdLookupHash == key && x.SemVerSortKey == semVerSortKey)
            .ToListAsync(cancellationToken));
        foreach (var candidate in rows)
            EfDesignSupport.EnsureDefinitionIdentity(definitionId, candidate.DefinitionId, "workflow definition version lookup");
        return rows.Count > 0;
    }
}
