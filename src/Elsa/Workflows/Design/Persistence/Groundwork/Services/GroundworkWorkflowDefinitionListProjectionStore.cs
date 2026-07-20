using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Resolves aggregate list facts with one bounded read of each relevant document kind and combines
/// them in memory. The number of document-store queries is independent of the definition count.
/// </summary>
public sealed class GroundworkWorkflowDefinitionListProjectionStore : IWorkflowDefinitionListProjectionStore
{
    private readonly GroundworkWorkflowDefinitionDraftDocumentStore _drafts;
    private readonly GroundworkNamedQueryAccess<WorkflowDefinitionVersion> _versions;

    public GroundworkWorkflowDefinitionListProjectionStore(
        IDocumentStore store,
        IBoundedDocumentStore boundedStore,
        IPayloadSerializer payloadSerializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IGroundworkStoreSessionFactory? sessions = null)
    {
        var serialization = GroundworkDesignDocumentSerialization.Create(payloadSerializer);
        _drafts = new GroundworkWorkflowDefinitionDraftDocumentStore(store, serialization, accessContextAccessor, boundedStore);
        _versions = new GroundworkNamedQueryAccess<WorkflowDefinitionVersion>(
            store,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            serialization,
            boundedStore,
            sessions,
            DesignPersistenceDomain.Workflow,
            "workflow definition list projection");
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
        var versionsTask = ListVersionsByDefinitionIdsAsync(definitionIds, cancellationToken);
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

    private Task<IReadOnlyList<WorkflowDefinitionVersion>> ListVersionsByDefinitionIdsAsync(
        IReadOnlyCollection<string> definitionIds,
        CancellationToken cancellationToken) =>
        _versions.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.QueryAsync(
                WorkflowsDesignStorageManifest.ListVersionsByDefinitionQuery,
                Query<WorkflowDefinitionVersion>.Where(
                    x => x.DefinitionId,
                    QueryOp.In,
                    definitionIds),
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionOrder,
                token),
            cancellationToken);
}
