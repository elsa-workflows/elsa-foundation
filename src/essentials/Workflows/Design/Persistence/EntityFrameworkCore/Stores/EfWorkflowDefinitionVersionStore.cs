using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowDefinitionVersionStore(WorkflowsDesignDbContext db, IPayloadSerializer serializer, IWorkflowDefinitionStore definitions, IPersistenceAccessContextAccessor access) : IWorkflowDefinitionVersionStore
{
    /// Ids per query, comfortably under every supported provider's host-parameter ceiling.
    private const int ProviderSafeIdBatchSize = 200;

    private IQueryable<WorkflowDefinitionVersion> Query() => EfDesignSupport.InScope(db.Versions, access, x => x.TenantId);
    public async Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => await FindByIdAsync(versionId, cancellationToken) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionVersion), versionId);
    public async Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var idHash = EfDesignSupport.LookupHash(versionId);
        var row = await EfDesignSupport.ReadAsync("reading workflow definition version", () => Query().SingleOrDefaultAsync(x => x.IdLookupHash == idHash, cancellationToken));
        if (row is null)
            return null;
        EfDesignSupport.EnsurePhysicalScope(db, row, "workflow definition version lookup");
        EfDesignSupport.EnsureExactIdentity(versionId, row.Id, "workflow definition version lookup");
        return EfDesignSupport.MapVersion(serializer, row);
    }
    /// <summary>
    /// Loads many versions in one read per chunk. Every per-row check <see cref="FindByIdAsync"/> applies is applied
    /// here too: the lookup is by hashed identity and ends in an exact residual comparison, and one hash resolving to
    /// more than one row is refused rather than silently reduced. So a hash collision or a drifted row fails closed
    /// exactly as it does for a single read instead of aliasing another version. Ids that resolve to no row are
    /// simply absent from the result, which is what <c>null</c> means for the single read.
    /// </summary>
    public async Task<IReadOnlyList<WorkflowDefinitionVersion>> FindByIdsAsync(IReadOnlyCollection<string> versionIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(versionIds);
        var requested = versionIds.Distinct(StringComparer.Ordinal).ToArray();
        if (requested.Length == 0)
            return [];

        var byHash = requested.ToLookup(EfDesignSupport.LookupHash, StringComparer.Ordinal);
        var found = new List<WorkflowDefinitionVersion>(requested.Length);
        foreach (var chunk in byHash.Select(group => group.Key).Chunk(ProviderSafeIdBatchSize))
        {
            var rows = await EfDesignSupport.ReadAsync("reading workflow definition versions", () => Query()
                .Where(x => chunk.Contains(x.IdLookupHash))
                .ToListAsync(cancellationToken));
            foreach (var group in rows.GroupBy(row => row.IdLookupHash, StringComparer.Ordinal))
            {
                if (group.Count() > 1)
                    throw new InvalidDataException("The workflow definition version identity resolves to more than one row.");

                var row = group.Single();
                EfDesignSupport.EnsurePhysicalScope(db, row, "workflow definition version lookup");

                // Assert the exact identity the caller asked for. When no requested id matches the row, the first
                // one that shares its hash is passed in deliberately, so the assertion fails with the same message
                // the single read produces instead of the row being returned under a identity that is not its own.
                var candidates = byHash[row.IdLookupHash].ToArray();
                var exact = Array.Find(candidates, id => StringComparer.Ordinal.Equals(id, row.Id)) ?? candidates[0];
                EfDesignSupport.EnsureExactIdentity(exact, row.Id, "workflow definition version lookup");
                found.Add(EfDesignSupport.MapVersion(serializer, row));
            }
        }

        return found;
    }

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
        {
            EfDesignSupport.EnsurePhysicalScope(db, candidate, "latest workflow definition version lookup");
            EfDesignSupport.EnsureDefinitionIdentity(definitionId, candidate.DefinitionId, "latest workflow definition version lookup");
        }
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
        {
            EfDesignSupport.EnsurePhysicalScope(db, candidate, "workflow definition version lookup");
            EfDesignSupport.EnsureDefinitionIdentity(definitionId, candidate.DefinitionId, "workflow definition version lookup");
        }
        return rows.Select(x => EfDesignSupport.MapVersion(serializer, x)).ToArray();
    }

    public async Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
    {
        var key = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(definitionId));
        var rows = await EfDesignSupport.ReadAsync("checking workflow definition version", () => Query()
            .Where(x => x.DefinitionIdLookupHash == key && x.SemVerSortKey == semVerSortKey)
            .ToListAsync(cancellationToken));
        foreach (var candidate in rows)
        {
            EfDesignSupport.EnsurePhysicalScope(db, candidate, "workflow definition version lookup");
            EfDesignSupport.EnsureDefinitionIdentity(definitionId, candidate.DefinitionId, "workflow definition version lookup");
        }
        return rows.Count > 0;
    }
}
