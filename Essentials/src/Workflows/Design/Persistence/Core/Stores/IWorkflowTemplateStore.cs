using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>Tenant-scoped template reads and atomic, revision-checked document mutations.</summary>
public interface IWorkflowTemplateStore
{
    Task<IReadOnlyList<WorkflowTemplate>> ListAsync(CancellationToken cancellationToken = default);
    Task<WorkflowTemplate?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task<WorkflowTemplate> SaveAsync(DesignOperationKey operationKey, string? id, string name, string? description,
        WorkflowTemplateContent content, string? expectedRevision, CancellationToken cancellationToken = default);
    Task DeleteAsync(DesignOperationKey operationKey, string id, string expectedRevision, CancellationToken cancellationToken = default);
}
