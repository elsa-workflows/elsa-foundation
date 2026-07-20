using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkDeleteWorkflowDefinitionPermanentlyCommand(
    IDocumentStore store,
    IDesignAtomicWriter atomicWrite,
    IPayloadSerializer payloadSerializer,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionDraftStore draftStore,
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IEnumerable<IWorkflowDefinitionPermanentDeletionGuard>? deletionGuards = null,
    ILogger<GroundworkDeleteWorkflowDefinitionPermanentlyCommand>? logger = null)
    : IDeleteWorkflowDefinitionPermanentlyCommand
{
    private const string OperationKind = "workflow.definition.permanent-delete.v1";

    public async Task Execute(
        DesignOperationKey operationKey,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        List<DeleteDocumentRequest>? deletes = null;
        PermanentDeleteResult? resolvedResult = null;
        var outcome = await GroundworkDesignAtomicCommand.ExecuteAsync(
            atomicWrite,
            operationKey,
            OperationKind,
            new PermanentDeleteRequestMaterial(definitionId),
            [
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind
            ],
            async (context, token) =>
            {
                var stagedDeletes = deletes ?? throw new InvalidOperationException(
                    "Permanent-delete aggregate resolution did not complete before staging.");
                foreach (var delete in stagedDeletes)
                    await context.DeleteAsync(delete, token);
                return resolvedResult!;
            },
            cancellationToken: cancellationToken,
            beforeAttempt: async token =>
            {
                var definition = await definitionStore.FindByIdAsync(definitionId, token)
                                 ?? throw new ArgumentException(
                                     $"Workflow definition '{definitionId}' was not found.");
                accessContextAccessor.Current.EnsureTenantScope(definition.TenantId);
                if (definition.DeletedAt is null)
                {
                    throw new InvalidOperationException(
                        "A workflow definition must be soft-deleted before permanent deletion.");
                }

                foreach (var guard in deletionGuards ?? [])
                    await guard.EnsureCanDeleteAsync(definitionId, token);

                var resolvedDeletes = new List<DeleteDocumentRequest>();
                var drafts = await draftStore.ListByWorkflowDefinitionIdAsync(definitionId, token);
                foreach (var draft in drafts)
                    accessContextAccessor.Current.EnsureTenantScope(draft.TenantId);
                if (drafts.Count > 0)
                {
                    var draftDocuments = new GroundworkWorkflowDefinitionDraftDocumentStore(
                        store,
                        GroundworkDesignDocumentSerialization.Create(payloadSerializer),
                        accessContextAccessor);
                    resolvedDeletes.AddRange(drafts.Select(draft => draftDocuments.ToDeleteRequest(draft.Id)));
                }

                var versions = await versionStore.ListByDefinitionAsync(definitionId, token);
                foreach (var version in versions)
                {
                    accessContextAccessor.Current.EnsureTenantScope(version.TenantId);
                    var layout = await layoutStore.FindByVersionIdAsync(version.Id, token);
                    if (layout is not null)
                    {
                        accessContextAccessor.Current.EnsureTenantScope(layout.TenantId);
                        resolvedDeletes.Add(GroundworkDocumentWriter.ToDeleteRequest(
                            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
                            layout.Id));
                    }

                    resolvedDeletes.Add(GroundworkDocumentWriter.ToDeleteRequest(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                        version.Id));
                }

                resolvedDeletes.Add(GroundworkDocumentWriter.ToDeleteRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    definitionId));
                deletes = resolvedDeletes;
                resolvedResult = new PermanentDeleteResult(
                    definitionId,
                    versions.Count,
                    drafts.Count,
                    definition.DeletedAt.Value,
                    definition.DeletedReason);
            });

        if (outcome.ShouldPublishPostCommitOutcome)
        {
            logger?.LogInformation(
                "Permanently deleted workflow definition {DefinitionId} ({VersionCount} version(s), {DraftCount} draft(s)); soft-deleted at {DeletedAt} with reason {DeletedReason}",
                outcome.Value.DefinitionId,
                outcome.Value.VersionCount,
                outcome.Value.DraftCount,
                outcome.Value.DeletedAt,
                outcome.Value.DeletedReason);
        }
    }

    private sealed record PermanentDeleteRequestMaterial(string DefinitionId);

    private sealed record PermanentDeleteResult(
        string DefinitionId,
        int VersionCount,
        int DraftCount,
        DateTimeOffset DeletedAt,
        string? DeletedReason);
}
