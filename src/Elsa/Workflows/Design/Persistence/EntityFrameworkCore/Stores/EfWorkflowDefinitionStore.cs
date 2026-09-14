using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowDefinitionStore(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access) : IWorkflowDefinitionStore
{
    private IQueryable<WorkflowDefinition> Query() => EfDesignSupport.InScope(db.Definitions.AsNoTracking(), access, x => x.TenantId);
    public async Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => await FindByIdAsync(id, cancellationToken) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), id);
    public async Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var lookupHash = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(id));
        var row = await EfDesignSupport.ReadAsync("reading workflow definition", () => Query().SingleOrDefaultAsync(
            x => x.IdLookupHash == lookupHash, cancellationToken));
        if (row is not null)
            EfDesignSupport.EnsureDefinitionIdentity(id, row.Id, "workflow definition lookup");
        return row;
    }
    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.TenantAgnostic == true && !access.Current.AcrossScopes)
            throw new InvalidOperationException("Tenant-agnostic workflow reads require privileged across-scope access.");
        var query = filter.TenantAgnostic == true ? db.Definitions.AsNoTracking() : Query();
        if (filter.Id is not null)
        {
            var lookupHash = EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(filter.Id));
            query = query.Where(x => x.IdLookupHash == lookupHash);
        }
        if (filter.Ids is not null)
        {
            if (filter.Ids.Count == 0) return [];
            var lookupHashes = filter.Ids.Select(EfDesignSupport.SearchKey).Select(EfDesignSupport.LookupHash).ToArray();
            query = query.Where(x => lookupHashes.Contains(x.IdLookupHash));
        }
        if (filter.Name is not null) query = query.Where(x => x.Name == filter.Name);
        if (filter.Names is not null) { if (filter.Names.Count == 0) return []; query = query.Where(x => filter.Names.Contains(x.Name)); }
        if (filter.Description is not null) query = query.Where(x => x.Description == filter.Description);
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = EfDesignSupport.SearchKey(filter.SearchTerm);
            query = query.Where(x =>
                x.IdSearchKey.Contains(term) ||
                x.NameSearchKey!.Contains(term) ||
                (x.DescriptionSearchKey != null && x.DescriptionSearchKey.Contains(term)));
        }
        var exactRoute = filter.Id is not null || filter.Ids is not null || filter.Name is not null ||
                         filter.Names is not null || filter.Description is not null;
        var values = await EfDesignSupport.ReadAsync("listing workflow definitions", () =>
            (exactRoute || string.IsNullOrWhiteSpace(filter.SearchTerm)
                ? query
                : query.Take(10_001))
            .OrderBy(x => x.Id).ThenBy(x => x.TenantId).ToListAsync(cancellationToken));
        if (!exactRoute && !string.IsNullOrWhiteSpace(filter.SearchTerm) && values.Count > 10_000)
            throw new InvalidOperationException("Workflow definition search exceeded the bounded result limit of 10000.");
        IEnumerable<WorkflowDefinition> validated = values;
        if (filter.Id is not null)
        {
            foreach (var candidate in values)
                EfDesignSupport.EnsureDefinitionIdentity(filter.Id, candidate.Id, "workflow definition lookup");
        }
        else if (filter.Ids is not null)
        {
            foreach (var candidate in values)
                EfDesignSupport.EnsureDefinitionIdentityInSet(filter.Ids, candidate.Id, "workflow definition lookup");
        }
        if (filter.Name is not null)
            validated = validated.Where(value => StringComparer.Ordinal.Equals(value.Name, filter.Name));
        if (filter.Names is not null)
            validated = validated.Where(value => filter.Names.Contains(value.Name, StringComparer.Ordinal));
        if (filter.Description is not null)
            validated = validated.Where(value => StringComparer.Ordinal.Equals(value.Description, filter.Description));
        return validated
            .OrderBy(value => value.Id, StringComparer.Ordinal)
            .ThenBy(value => value.TenantId, StringComparer.Ordinal)
            .ToArray();
    }
}
