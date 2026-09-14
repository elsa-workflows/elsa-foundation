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
    public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => Query().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.TenantAgnostic == true && !access.Current.AcrossScopes)
            throw new InvalidOperationException("Tenant-agnostic workflow reads require privileged across-scope access.");
        var query = filter.TenantAgnostic == true ? db.Definitions.AsNoTracking() : Query();
        if (filter.Id is not null) query = query.Where(x => x.Id == filter.Id);
        if (filter.Ids is not null) { if (filter.Ids.Count == 0) return []; query = query.Where(x => filter.Ids.Contains(x.Id)); }
        if (filter.Name is not null) query = query.Where(x => x.Name == filter.Name);
        if (filter.Names is not null) { if (filter.Names.Count == 0) return []; query = query.Where(x => filter.Names.Contains(x.Name)); }
        if (filter.Description is not null) query = query.Where(x => x.Description == filter.Description);
        var values = await query.OrderBy(x => x.Id).ThenBy(x => x.TenantId).Take(1000).ToListAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(filter.SearchTerm)) return values;
        return values.Where(x => Contains(x.Id, filter.SearchTerm) || Contains(x.Name, filter.SearchTerm) || Contains(x.Description, filter.SearchTerm)).ToArray();
    }
    private static bool Contains(string? value, string term) => value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
