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
public sealed class GroundworkWorkflowDefinitionVersionStore : IWorkflowDefinitionVersionStore
{
    private readonly IWorkflowDefinitionStore definitions;
    private readonly GroundworkDesignStorage storage;
    private readonly System.Text.Json.JsonSerializerOptions json;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;

    public GroundworkWorkflowDefinitionVersionStore(
        IGroundworkStorageSessionSource sessions,
        IWorkflowDefinitionStore definitions,
        IPayloadSerializer payloadSerializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        this.definitions = definitions;
        storage = new GroundworkDesignStorage(sessions, accessContextAccessor, targetName);
        json = GroundworkDesignDocumentSerialization.Create(payloadSerializer);
        this.accessContextAccessor = accessContextAccessor;
    }

    private GroundworkWorkflowDefinitionVersionStore(
        GroundworkDesignStorage storage,
        IWorkflowDefinitionStore definitions,
        System.Text.Json.JsonSerializerOptions json,
        IPersistenceAccessContextAccessor accessContextAccessor)
    {
        this.storage = storage;
        this.definitions = definitions;
        this.json = json;
        this.accessContextAccessor = accessContextAccessor;
    }

    internal GroundworkWorkflowDefinitionVersionStore ForStorage(GroundworkDesignStorage boundStorage) =>
        new(boundStorage, definitions, json, accessContextAccessor);

    public async Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
        await FindByIdAsync(versionId, cancellationToken) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionVersion), versionId);

    public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = storage.Read(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, versionId);
        if (entry is null)
            return Task.FromResult<WorkflowDefinitionVersion?>(null);
        var version = storage.MapVersion(entry, json);
        accessContextAccessor.Current.EnsureTenantScope(version.TenantId);
        GroundworkDesignStorage.EnsureProjectedIdentity(entry, version, "workflow version point read");
        return Task.FromResult<WorkflowDefinitionVersion?>(version);
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
        var versions = rows.Select(row =>
        {
            var version = storage.MapVersion(row, json);
            accessContextAccessor.Current.EnsureTenantScope(version.TenantId);
            GroundworkDesignStorage.EnsureProjectedIdentity(row, version, "workflow version relationship lookup");
            return version;
        }).ToArray();
        foreach (var version in versions)
            GroundworkDesignStorage.EnsureDefinitionIdentity(
                definitionId,
                version.DefinitionId,
                "workflow version relationship lookup");
        return versions.FirstOrDefault();
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
                storage.Order(unit, WorkflowsDesignStorageManifest.VersionDefinitionIdLookupHashField),
                storage.Order(unit, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField),
                storage.Order(unit, WorkflowsDesignStorageManifest.VersionIdField)
            ],
            WorkflowsDesignStorageManifest.VersionByDefinitionIndex,
            cancellationToken: cancellationToken);
        var versions = rows.Select(row =>
        {
            var version = storage.MapVersion(row, json);
            accessContextAccessor.Current.EnsureTenantScope(version.TenantId);
            GroundworkDesignStorage.EnsureProjectedIdentity(row, version, "workflow version relationship lookup");
            return version;
        }).ToArray();
        foreach (var version in versions)
            GroundworkDesignStorage.EnsureDefinitionIdentity(
                definitionId,
                version.DefinitionId,
                "workflow version relationship lookup");
        return Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(versions);
    }

    public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unit = WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind;
        var rows = storage.Query(
            unit,
            new Predicate.And([
                storage.Equal(unit, WorkflowsDesignStorageManifest.VersionDefinitionIdField, definitionId),
                storage.Equal(unit, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField, semVerSortKey)
            ]),
            [storage.Order(unit, WorkflowsDesignStorageManifest.VersionIdField)],
            WorkflowsDesignStorageManifest.VersionByDefinitionAndSortKeyIndex,
            cancellationToken: cancellationToken);
        var versions = rows.Select(row =>
        {
            var version = storage.MapVersion(row, json);
            accessContextAccessor.Current.EnsureTenantScope(version.TenantId);
            GroundworkDesignStorage.EnsureProjectedIdentity(row, version, "workflow version relationship lookup");
            return version;
        }).ToArray();
        foreach (var version in versions)
            GroundworkDesignStorage.EnsureDefinitionIdentity(
                definitionId,
                version.DefinitionId,
                "workflow version relationship lookup");
        return Task.FromResult(versions.Length > 0);
    }
}
