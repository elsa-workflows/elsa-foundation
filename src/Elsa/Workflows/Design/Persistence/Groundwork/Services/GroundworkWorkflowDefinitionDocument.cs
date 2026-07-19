using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

internal sealed record GroundworkWorkflowDefinitionDocument(string Collection, WorkflowDefinition Entity, string SearchId);

internal static class GroundworkWorkflowDefinitionDocumentWriter
{
    public static SaveDocumentRequest ToSaveRequest(WorkflowDefinition definition, PersistenceAccessContext accessContext)
    {
        accessContext.EnsureTenantScope(definition.TenantId);
        return JsonDocumentStoreExtensions.ToSaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            new GroundworkWorkflowDefinitionDocument(WorkflowsDesignStorageManifest.WorkflowDefinitionCollection, definition, definition.Id),
            GroundworkDesignJson.Options);
    }
}
