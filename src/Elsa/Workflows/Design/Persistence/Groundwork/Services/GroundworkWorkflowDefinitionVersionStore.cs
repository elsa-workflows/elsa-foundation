using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>Public Groundwork v2 implementation of the workflow-definition-version read port.</summary>
public sealed class GroundworkWorkflowDefinitionVersionStore(
    IGroundworkStorageSessionSource sessions,
    IWorkflowDefinitionStore definitions,
    IPayloadSerializer payloadSerializer,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null) : IWorkflowDefinitionVersionStore
{
    private readonly GroundworkDesignStorage storage = new(sessions, accessContextAccessor, targetName);
    private readonly System.Text.Json.JsonSerializerOptions json =
        GroundworkDesignDocumentSerialization.Create(payloadSerializer);

    public async Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
        await FindByIdAsync(versionId, cancellationToken) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionVersion), versionId);

    public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = storage.Read(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, versionId);
        return Task.FromResult(entry is null ? null : storage.MapVersion(entry, json));
    }

    public async Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var version = await GetAsync(versionId, cancellationToken);
        version.Definition = await definitions.GetAsync(version.DefinitionId, cancellationToken);
        return version;
    }

    public async Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unit = WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind;
        var rows = storage.Query(
            unit,
            storage.Equal(unit, WorkflowsDesignStorageManifest.VersionDefinitionIdField, definitionId),
            [
                storage.Order(unit, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField, descending: true),
                storage.Order(unit, WorkflowsDesignStorageManifest.VersionIdField, descending: true)
            ],
            WorkflowsDesignStorageManifest.LatestVersionByDefinitionIndex,
            cancellationToken: cancellationToken);
        return rows.Select(row => storage.MapVersion(row, json)).FirstOrDefault();
    }

    public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unit = WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind;
        var rows = storage.Query(
            unit,
            storage.Equal(unit, WorkflowsDesignStorageManifest.VersionDefinitionIdField, definitionId),
            [
                storage.Order(unit, WorkflowsDesignStorageManifest.VersionDefinitionIdField),
                storage.Order(unit, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField),
                storage.Order(unit, WorkflowsDesignStorageManifest.VersionIdField)
            ],
            WorkflowsDesignStorageManifest.VersionByDefinitionIndex,
            cancellationToken: cancellationToken);
        return Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(
            rows.Select(row => storage.MapVersion(row, json)).ToArray());
    }

    public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unit = WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind;
        return Task.FromResult(storage.Any(
            unit,
            new Predicate.And([
                storage.Equal(unit, WorkflowsDesignStorageManifest.VersionDefinitionIdField, definitionId),
                storage.Equal(unit, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField, semVerSortKey)
            ]),
            WorkflowsDesignStorageManifest.VersionByDefinitionAndSortKeyIndex,
            cancellationToken: cancellationToken));
    }
}
