using System.Text.Json;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

internal sealed record GroundworkWorkflowDefinitionDocument(
    string Collection,
    WorkflowDefinition Entity,
    string MarkerProjection,
    string ControlledProjection = "|");

internal static class GroundworkWorkflowDefinitionDocuments
{
    public static GroundworkWorkflowDefinitionDocument Deserialize(DocumentEnvelope envelope)
    {
        var document = JsonSerializer.Deserialize<GroundworkWorkflowDefinitionDocument>(
            envelope.ContentJson,
            GroundworkDesignJson.Options);
        if (document is not null)
            return document with
            {
                MarkerProjection = string.IsNullOrEmpty(document.MarkerProjection)
                    ? GroundworkWorkflowDefinitionTagStore.MarkerProjection([])
                    : document.MarkerProjection,
                ControlledProjection = string.IsNullOrEmpty(document.ControlledProjection)
                    ? GroundworkWorkflowDefinitionTagStore.ControlledProjection([])
                    : document.ControlledProjection
            };

        throw new InvalidOperationException(
            $"Workflow definition document '{envelope.Id}' could not be deserialized.");
    }

    public static SaveDocumentRequest Save(
        WorkflowDefinition entity,
        string markerProjection,
        long? expectedVersion = null,
        string? controlledProjection = null) =>
        new(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            entity.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(
                new GroundworkWorkflowDefinitionDocument(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    entity,
                    markerProjection,
                    controlledProjection ?? GroundworkWorkflowDefinitionTagStore.ControlledProjection([])),
                GroundworkDesignJson.Options),
            expectedVersion);
}
