using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Creates a workflow definition and its first logical draft as one replay-safe Groundwork transition.
/// </summary>
public sealed class GroundworkAddWorkflowDefinitionCommand(
    GroundworkDesignStorage storage,
    IDesignAtomicWriter atomicWrite,
    IPayloadSerializer payloadSerializer,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IAddWorkflowDefinitionCommand
{
    private const string OperationKind = "workflow.definition.create.v1";

    public Task<WorkflowDefinitionCreated> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition workflowDefinition,
        WorkflowDefinitionDraft draft,
        CancellationToken cancellationToken = default) =>
        Execute(operationKey, workflowDefinition, draft, [], [], cancellationToken);

    public Task<WorkflowDefinitionCreated> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition workflowDefinition,
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout,
        CancellationToken cancellationToken = default) =>
        Execute(
            operationKey,
            workflowDefinition,
            draft,
            layout,
            [],
            cancellationToken);

    public async Task<WorkflowDefinitionCreated> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition workflowDefinition,
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout,
        IReadOnlyCollection<ActivityPresentationRecord> activityPresentation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(workflowDefinition);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(activityPresentation);
        var normalizedPresentation =
            ActivityPresentationRecord.NormalizeCollection(activityPresentation);
        accessContextAccessor.Current.EnsureTenantScope(workflowDefinition.TenantId);
        accessContextAccessor.Current.EnsureTenantScope(draft.TenantId);
        if (!StringComparer.Ordinal.Equals(workflowDefinition.Id, draft.WorkflowDefinitionId))
        {
            throw new ArgumentException(
                "The first workflow draft must belong to the workflow definition being created.",
                nameof(draft));
        }

        var requestMaterial = new CreateRequestMaterial(
            workflowDefinition.Name,
            workflowDefinition.Description,
            workflowDefinition.DeletedAt,
            workflowDefinition.DeletedReason,
            workflowDefinition.IsSourceOwned,
            draft.SourceVersionId,
            GroundworkDesignSerialization.Execute(
                DesignPersistenceDomain.Workflow,
                OperationKind,
                "workflow definition draft",
                () => payloadSerializer.Serialize(draft.State)),
            layout.Select(ToMaterial).ToArray(),
            normalizedPresentation.Select(ToMaterial).ToArray());

        var now = clock.UtcNow;
        GroundworkEntityTimestamps.StampAdded(workflowDefinition, now);
        GroundworkEntityTimestamps.StampAdded(draft, now);

        var definitionSave = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            workflowDefinition,
            GroundworkDesignJson.Options,
            accessContextAccessor.Current,
            persistenceDomain: DesignPersistenceDomain.Workflow);

        var draftDocuments = new GroundworkWorkflowDefinitionDraftDocumentStore(
            storage,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor);
        var draftSave = draftDocuments.ToSaveRequest(
            draft,
            layout.ToArray(),
            normalizedPresentation) with
        { ExpectedVersion = 0 };
        definitionSave = definitionSave with { ExpectedVersion = 0 };

        var result = await GroundworkDesignAtomicCommand.ExecuteAsync(
            atomicWrite,
            operationKey,
            OperationKind,
            requestMaterial,
            [
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind
            ],
            async (context, token) =>
            {
                await context.SaveAsync(definitionSave, token);
                await context.SaveAsync(draftSave, token);
                return new WorkflowDefinitionCreated(workflowDefinition.Id, draft.Id);
            },
            cancellationToken: cancellationToken);
        return result.Value;
    }

    private static LayoutMaterial ToMaterial(DesignMetadataRecord record) =>
        new(
            record.NodeId,
            record.X,
            record.Y,
            record.Width,
            record.Height,
            record.AdditionalProperties?.GetRawText());

    private static ActivityPresentationMaterial ToMaterial(ActivityPresentationRecord record) =>
        new(record.NodeId, record.DisplayName, record.Description);

    private sealed record CreateRequestMaterial(
        string Name,
        string? Description,
        DateTimeOffset? DeletedAt,
        string? DeletedReason,
        bool IsSourceOwned,
        string? SourceVersionId,
        string StateJson,
        IReadOnlyCollection<LayoutMaterial> Layout,
        IReadOnlyCollection<ActivityPresentationMaterial> ActivityPresentation);

    private sealed record LayoutMaterial(
        string NodeId,
        double X,
        double Y,
        double? Width,
        double? Height,
        string? AdditionalPropertiesJson);

    private sealed record ActivityPresentationMaterial(
        string NodeId,
        string? DisplayName,
        string? Description);
}
