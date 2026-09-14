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
    public async Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) { var row = await Query().SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken); return row is null ? null : EfDesignSupport.MapVersion(serializer, row); }
    public async Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) { var row = await GetAsync(versionId, cancellationToken); row.Definition = await definitions.GetAsync(row.DefinitionId, cancellationToken); return row; }
    public async Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) { var row = await Query().Where(x => x.DefinitionId == definitionId).OrderByDescending(x => x.SemVerSortKey).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken); return row is null ? null : EfDesignSupport.MapVersion(serializer, row); }
    public async Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => (await Query().Where(x => x.DefinitionId == definitionId).OrderBy(x => x.SemVerSortKey).ThenBy(x => x.Id).ToListAsync(cancellationToken)).Select(x => EfDesignSupport.MapVersion(serializer, x)).ToArray();
    public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => Query().AnyAsync(x => x.DefinitionId == definitionId && x.SemVerSortKey == semVerSortKey, cancellationToken);
}
