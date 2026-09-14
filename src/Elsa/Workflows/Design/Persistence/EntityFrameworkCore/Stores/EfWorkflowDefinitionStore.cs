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
    public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
        EfDesignSupport.ReadAsync("reading workflow definition", () => Query().SingleOrDefaultAsync(
            x => EF.Property<string>(x, "IdSearchKey") == EfDesignSupport.SearchKey(id), cancellationToken));
    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.TenantAgnostic == true && !access.Current.AcrossScopes)
            throw new InvalidOperationException("Tenant-agnostic workflow reads require privileged across-scope access.");
        var query = filter.TenantAgnostic == true ? db.Definitions.AsNoTracking() : Query();
        if (filter.Id is not null) query = query.Where(x => EF.Property<string>(x, "IdSearchKey") == EfDesignSupport.SearchKey(filter.Id));
        if (filter.Ids is not null) { if (filter.Ids.Count == 0) return []; var ids = filter.Ids.Select(EfDesignSupport.SearchKey).ToArray(); query = query.Where(x => ids.Contains(EF.Property<string>(x, "IdSearchKey"))); }
        if (filter.Name is not null) query = query.Where(x => x.Name == filter.Name);
        if (filter.Names is not null) { if (filter.Names.Count == 0) return []; query = query.Where(x => filter.Names.Contains(x.Name)); }
        if (filter.Description is not null) query = query.Where(x => x.Description == filter.Description);
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = EfDesignSupport.SearchKey(filter.SearchTerm);
            query = query.Where(x =>
                EF.Property<string?>(x, "IdSearchKey")!.Contains(term) ||
                EF.Property<string?>(x, "NameSearchKey")!.Contains(term) ||
                (EF.Property<string?>(x, "DescriptionSearchKey") != null && EF.Property<string?>(x, "DescriptionSearchKey")!.Contains(term)));
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
        return values;
    }
}
