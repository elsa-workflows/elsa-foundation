using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Resolves aggregate list facts with one bounded read of each relevant document kind and combines
/// them in memory. The number of document-store queries is independent of the definition count.
/// </summary>
public sealed class GroundworkWorkflowDefinitionListProjectionStore : IWorkflowDefinitionListProjectionStore
{
    private readonly GroundworkWorkflowDefinitionDraftDocumentStore _drafts;
    private readonly GroundworkReadStore<WorkflowDefinitionVersion> _versions;

    public GroundworkWorkflowDefinitionListProjectionStore(
        IDocumentStore store,
        IBoundedDocumentStore boundedStore,
        IPayloadSerializer payloadSerializer,
        IPersistenceAccessContextAccessor accessContextAccessor)
    {
        var serialization = GroundworkDesignDocumentSerialization.Create(payloadSerializer);
        _drafts = new GroundworkWorkflowDefinitionDraftDocumentStore(store, serialization, accessContextAccessor, boundedStore);
        _versions = new GroundworkReadStore<WorkflowDefinitionVersion>(
            store,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            WorkflowsDesignStorageManifest.ListAllQuery,
            WorkflowsDesignStorageManifest.CollectionField,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
            serialization,
            boundedStore);
    }

    public GroundworkWorkflowDefinitionListProjectionStore(
        IDocumentStore store,
        IPayloadSerializer payloadSerializer,
        IPersistenceAccessContextAccessor accessContextAccessor)
        : this(
            store,
            store as IBoundedDocumentStore ?? throw new InvalidOperationException(
                "Workflow-definition projection queries require an admitted bounded document-store runtime."),
            payloadSerializer,
            accessContextAccessor)
    {
    }

    public async Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        var definitionIds = workflowDefinitionIds.Distinct(StringComparer.Ordinal).ToArray();
        if (definitionIds.Length == 0)
            return [];

        var draftsTask = _drafts.ListByWorkflowDefinitionIdsAsync(definitionIds, cancellationToken);
        var versionsTask = _versions.QueryAsync(
            Query<WorkflowDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.In, definitionIds),
            cancellationToken);
        await Task.WhenAll(draftsTask, versionsTask);
        var drafts = await draftsTask;
        var versionRows = await versionsTask;

        var currentDrafts = drafts
            .GroupBy(x => x.Entity.WorkflowDefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(x => x.Entity.LastModifiedAt)
                    .ThenByDescending(x => x.Entity.CreatedAt)
                    .ThenByDescending(x => x.Entity.Id, StringComparer.Ordinal)
                    .First()
                    .Entity,
                StringComparer.Ordinal);
        var versions = versionRows
            .GroupBy(x => x.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return definitionIds
            .Select(definitionId =>
            {
                currentDrafts.TryGetValue(definitionId, out var draft);
                versions.TryGetValue(definitionId, out var definitionVersions);
                var latest = definitionVersions?.FirstOrDefault();
                return new WorkflowDefinitionListProjection(
                    definitionId,
                    draft?.Id,
                    latest?.Id,
                    latest?.Version,
                    definitionVersions?.Length ?? 0);
            })
            .ToArray();
    }
}
