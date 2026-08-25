using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkDeleteWorkflowDefinitionPermanentlyCommand(
    GroundworkDesignStorage storage,
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
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        var guards = deletionGuards?.ToArray() ?? [];

        List<GroundworkDesignDeleteRequest>? deletes = null;
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
                // Refuse before touching any definition row: whether this host may permanently delete at all is a
                // property of its composition, not of the definition. A design-only host composes no publication
                // check, so it cannot tell whether another node still holds a live publication against the same
                // design catalog, and the delete is unrecoverable. Keying on the publication guard rather than on
                // the guard list being empty keeps the refusal meaning "no publication check" once other verticals
                // contribute vetoes of their own. The check sits INSIDE beforeAttempt — after the atomic writer's
                // operation-marker replay lookup — so a retry of an already-committed delete that lands on a
                // design-only node still replays to success instead of refusing an operation that already happened.
                if (guards.OfType<IWorkflowDefinitionPublicationDeletionGuard>().Any() is false)
                    throw new PermanentDeletionUnavailableException(definitionId);

                var definition = await definitionStore.FindByIdAsync(definitionId, token)
                                 ?? throw EntityNotFoundException.ForEntity(
                                     typeof(WorkflowDefinition),
                                     definitionId);
                accessContextAccessor.Current.EnsureTenantScope(definition.TenantId);
                if (definition.DeletedAt is null)
                    throw new WorkflowDefinitionNotSoftDeletedException(definitionId);

                foreach (var guard in guards)
                    await guard.EnsureCanDeleteAsync(definitionId, token);

                var resolvedDeletes = new List<GroundworkDesignDeleteRequest>();
                var drafts = await draftStore.ListByWorkflowDefinitionIdAsync(definitionId, token);
                foreach (var draft in drafts)
                    accessContextAccessor.Current.EnsureTenantScope(draft.TenantId);
                if (drafts.Count > 0)
                {
                    foreach (var draft in drafts)
                    {
                        var current = storage.Read(
                            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                            draft.Id) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), draft.Id);
                        resolvedDeletes.Add(new GroundworkDesignDeleteRequest(
                            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                            draft.Id,
                            storage.Version(current) ?? throw new InvalidDataException(
                                $"Draft '{draft.Id}' did not return a provider version.")));
                    }
                }

                var versions = await versionStore.ListByDefinitionAsync(definitionId, token);
                foreach (var version in versions)
                {
                    accessContextAccessor.Current.EnsureTenantScope(version.TenantId);
                    var layout = await layoutStore.FindByVersionIdAsync(version.Id, token);
                    if (layout is not null)
                    {
                        accessContextAccessor.Current.EnsureTenantScope(layout.TenantId);
                        var currentLayout = storage.Read(
                            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
                            layout.Id) ?? throw EntityNotFoundException.ForEntity(
                                typeof(WorkflowDefinitionVersionLayout), layout.Id);
                        resolvedDeletes.Add(new GroundworkDesignDeleteRequest(
                            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
                            layout.Id,
                            storage.Version(currentLayout) ?? throw new InvalidDataException(
                                $"Layout '{layout.Id}' did not return a provider version.")));
                    }

                    var currentVersion = storage.Read(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                        version.Id) ?? throw EntityNotFoundException.ForEntity(
                            typeof(WorkflowDefinitionVersion), version.Id);
                    resolvedDeletes.Add(new GroundworkDesignDeleteRequest(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                        version.Id,
                        storage.Version(currentVersion) ?? throw new InvalidDataException(
                            $"Version '{version.Id}' did not return a provider version.")));
                }

                var currentDefinition = storage.Read(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    definitionId) ?? throw EntityNotFoundException.ForEntity(
                        typeof(WorkflowDefinition), definitionId);
                resolvedDeletes.Add(new GroundworkDesignDeleteRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    definitionId,
                    storage.Version(currentDefinition) ?? throw new InvalidDataException(
                        $"Definition '{definitionId}' did not return a provider version.")));
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
