using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>Groundwork implementation of the atomic definition-placement command.</summary>
public sealed class GroundworkMoveWorkflowDefinitionsCommand(
    IDocumentStore store,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor) : IMoveWorkflowDefinitionsCommand
{
    public async Task Execute(IReadOnlyCollection<string> definitionIds, string? folderId, CancellationToken cancellationToken = default)
    {
        Validate(definitionIds, folderId);
        var access = accessContextAccessor.Current;
        if (access.Scope is null)
            throw new InvalidOperationException("Workflow-definition moves require a tenant-scoped persistence context.");

        var scopeKinds = folderId is null
            ? new[] { WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind }
            : new[] { WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind };
        try
        {
            await using var unit = await store.BeginAsync(DocumentCommitScope.Of(scopeKinds), cancellationToken);
            await ValidateDestinationAsync(unit, folderId, access, cancellationToken);
            var movedAt = clock.UtcNow;

            // Finish every read and visibility check before staging a single mutation. A missing or foreign
            // member therefore remains non-disclosing and leaves the whole selection untouched.
            var definitions = new List<(DocumentEnvelope Envelope, WorkflowDefinition Entity)>();
            foreach (var definitionId in definitionIds)
            {
                var envelope = await unit.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, definitionId, cancellationToken)
                    ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), definitionId);
                var definition = ReadDefinition(envelope);
                try
                {
                    // Legacy and standard definition documents may omit TenantId; the ambient scoped
                    // document session remains authoritative. Explicit foreign tenant values are hidden.
                    access.EnsureTenantScope(definition.TenantId);
                }
                catch (InvalidOperationException)
                {
                    throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), definitionId);
                }
                definitions.Add((envelope, definition));
            }

            foreach (var (envelope, definition) in definitions)
            {
                definition.FolderId = folderId;
                // Placement moves are deliberately narrower than an entity save: they never repair or
                // otherwise mutate CreatedAt or authored/lifecycle fields.
                definition.LastModifiedAt = movedAt;
                var save = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    definition,
                    GroundworkDesignJson.Options,
                    access) with { ExpectedVersion = envelope.Version };
                var result = await unit.SaveAsync(save, cancellationToken);
                if (result.Status != DocumentStoreWriteStatus.Saved)
                    throw new WorkflowDefinitionFolderMoveConflictException();
            }
            await unit.CommitAsync(cancellationToken);
        }
        catch (DocumentAtomicWriteException exception) when (exception.Status is
            DocumentStoreWriteStatus.ConcurrencyConflict or
            DocumentStoreWriteStatus.IdentityConflict or
            DocumentStoreWriteStatus.NotFound)
        {
            throw new WorkflowDefinitionFolderMoveConflictException(exception);
        }
    }

    private static void Validate(IReadOnlyCollection<string> definitionIds, string? folderId)
    {
        if (definitionIds is null || definitionIds.Count == 0)
            throw new ArgumentException("At least one workflow definition must be selected.", nameof(definitionIds));
        if (definitionIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Workflow definition identifiers cannot be empty.", nameof(definitionIds));
        if (definitionIds.Distinct(StringComparer.Ordinal).Count() != definitionIds.Count)
            throw new ArgumentException("Workflow definition identifiers must be unique.", nameof(definitionIds));
        if (folderId is not null)
            WorkflowFolderNames.ValidateIdentifier(folderId, nameof(folderId));
    }

    private static async Task ValidateDestinationAsync(
        IDocumentUnitOfWork unit,
        string? folderId,
        PersistenceAccessContext access,
        CancellationToken cancellationToken)
    {
        if (folderId is null)
            return;
        var envelope = await unit.LoadAsync(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind, folderId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId);
        var folder = JsonSerializer.Deserialize<GroundworkDocument<WorkflowFolder>>(envelope.ContentJson, GroundworkDesignJson.Options)?.Entity
            ?? throw new InvalidOperationException("The workflow-folder document is empty.");
        try
        {
            access.EnsureTenantScope(folder.TenantId);
        }
        catch (InvalidOperationException)
        {
            throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId);
        }
    }

    private static WorkflowDefinition ReadDefinition(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<GroundworkDocument<WorkflowDefinition>>(envelope.ContentJson, GroundworkDesignJson.Options)?.Entity
        ?? throw new InvalidOperationException("The workflow-definition document is empty.");
}
