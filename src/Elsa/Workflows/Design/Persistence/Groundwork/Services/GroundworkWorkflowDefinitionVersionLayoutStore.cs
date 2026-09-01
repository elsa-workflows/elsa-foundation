using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>Public Groundwork v2 implementation of the version-layout read port.</summary>
public sealed class GroundworkWorkflowDefinitionVersionLayoutStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null) : IWorkflowDefinitionVersionLayoutStore
{
    private readonly GroundworkDesignStorage storage = new(sessions, accessContextAccessor, targetName);

    public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(
        string workflowDefinitionVersionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unit = WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind;
        var rows = storage.Query(
            unit,
            storage.Equal(unit, WorkflowsDesignStorageManifest.LayoutVersionIdField, workflowDefinitionVersionId),
            [storage.Order(unit, WorkflowsDesignStorageManifest.IdField)],
            WorkflowsDesignStorageManifest.LayoutByVersionIndex,
            cancellationToken: cancellationToken);
        return Task.FromResult<WorkflowDefinitionVersionLayout?>(
            rows.Select(row => storage.MapLayout(row, GroundworkDesignJson.Options)).FirstOrDefault());
    }
}
