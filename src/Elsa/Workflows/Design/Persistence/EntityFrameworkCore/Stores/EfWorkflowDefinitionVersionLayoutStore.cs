using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowDefinitionVersionLayoutStore(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access) : IWorkflowDefinitionVersionLayoutStore
{
    public async Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default)
    {
        var idHash = EfDesignSupport.LookupHash(workflowDefinitionVersionId);
        var row = await EfDesignSupport.ReadAsync("reading workflow version layout", () => EfDesignSupport.InScope(db.VersionLayouts, access, x => x.TenantId).Where(x => x.WorkflowDefinitionVersionIdLookupHash == idHash).SingleOrDefaultAsync(cancellationToken));
        if (row is null) return null;
        EfDesignSupport.EnsureExactIdentity(workflowDefinitionVersionId, row.WorkflowDefinitionVersionId, "workflow version layout lookup");
        var records = EfDesignSupport.ReadLayout(row.RecordsJson);
        var presentation = EfDesignSupport.ReadPresentation(row.ActivityPresentationJson);
        return new WorkflowDefinitionVersionLayout { Id = row.Id, TenantId = row.TenantId, WorkflowDefinitionVersionId = row.WorkflowDefinitionVersionId, CreatedAt = row.CreatedAt, LastModifiedAt = row.LastModifiedAt, Records = records, ActivityPresentation = presentation };
    }
}
