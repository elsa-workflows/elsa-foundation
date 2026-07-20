using System.Globalization;
using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowExecutionStateStore"/>. Ordinary collection projections and
/// execution-history paging both use admitted provider-neutral bounded routes.
/// </summary>
public sealed class GroundworkWorkflowExecutionStateStore : GroundworkDocumentStore, IWorkflowExecutionStateStore
{
    private readonly IPersistenceAccessContextAccessor _accessContextAccessor;
    private readonly IBoundedDocumentStore? _queries;

    public GroundworkWorkflowExecutionStateStore(
        IDocumentStore store,
        IGroundworkRuntimeDocumentSerializer serializer,
        IPersistenceAccessContextAccessor accessContextAccessor)
        : this(store, serializer, accessContextAccessor, null)
    {
    }

    public GroundworkWorkflowExecutionStateStore(
        IDocumentStore store,
        IGroundworkRuntimeDocumentSerializer serializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IBoundedDocumentStore? boundedStore = null)
        : base(store, serializer, ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind)
    {
        _accessContextAccessor = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));
        _queries = boundedStore ?? store as IBoundedDocumentStore;
    }

    private IBoundedDocumentStore Queries => _queries ?? throw new InvalidOperationException(
        "Workflow-execution queries require an admitted bounded document-store runtime.");

    public async ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        _accessContextAccessor.Current.EnsureTenantScope(state.TenantId);

        var document = WorkflowExecutionStateDocument.From(state);
        await SaveDocumentAsync(state.WorkflowExecutionId, document, cancellationToken);

        return state;
    }

    public async ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await LoadDocumentAsync<WorkflowExecutionStateDocument, WorkflowExecutionState>(
            workflowExecutionId, document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default)
        => await this.ListAllAsync(cancellationToken);

    internal async ValueTask<WorkflowExecutionStatePage> QueryFaultedForAttentionAsync(
        string tenantId,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (cursor is not null && string.IsNullOrWhiteSpace(cursor))
            throw new ArgumentException("The workflow runtime attention cursor cannot be blank.", nameof(cursor));
        _accessContextAccessor.Current.EnsureTenantScope(tenantId);

        DocumentQueryResult result;
        try
        {
            result = await Queries.QueryAsync(
                new DocumentQuery(
                    ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                    ElsaRuntimeStorageManifest.PageFaultedWorkflowExecutionsForAttentionQuery,
                    [
                        DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField,
                            tenantId)),
                        DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField,
                            ((int)WorkflowExecutionStatus.Faulted).ToString(CultureInfo.InvariantCulture)))
                    ],
                    [
                        new DocumentQueryOrder(
                            ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                            PhysicalSortDirection.Descending),
                        new DocumentQueryOrder(
                            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                            PhysicalSortDirection.Ascending)
                    ],
                    take: ElsaGroundworkQueryRoutes.MaximumResultCount,
                    continuation: cursor),
                cancellationToken);
        }
        catch (InvalidDocumentQueryContinuationException exception)
        {
            throw new ArgumentException(
                "The workflow runtime attention cursor is invalid or does not belong to this query.",
                nameof(cursor),
                exception);
        }
        var states = result.Documents
            .Select(Serializer.Deserialize<WorkflowExecutionStateDocument>)
            .Select(document => document.State)
            .ToArray();
        return new(states, result.NextContinuation, result.NextContinuation is not null, result.TotalCount);
    }

    public async ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        _accessContextAccessor.Current.EnsureTenantScope(query.TenantId);

        DocumentQueryResult result;
        try
        {
            result = await Queries.QueryAsync(
                new DocumentQuery(
                    ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                    ElsaRuntimeStorageManifest.PageWorkflowExecutionsQuery,
                    BuildHistoryClauses(query),
                    [
                        new DocumentQueryOrder(
                            ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                            PhysicalSortDirection.Descending),
                        new DocumentQueryOrder(
                            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                            PhysicalSortDirection.Ascending)
                    ],
                    take: query.PageSize,
                    continuation: query.Cursor),
                cancellationToken);
        }
        catch (InvalidDocumentQueryContinuationException exception)
        {
            throw new ArgumentException(
                "The workflow execution history cursor is invalid or does not belong to this query.",
                "cursor",
                exception);
        }

        var states = result.Documents
            .Select(Serializer.Deserialize<WorkflowExecutionStateDocument>)
            .Select(document => document.State)
            .ToArray();
        return new(
            states,
            result.NextContinuation,
            result.NextContinuation is not null,
            result.TotalCount);
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
    {
        var artifactIds = new List<string>();
        for (var skip = 0;; skip += RuntimeStorePageRequest.MaximumLimit)
        {
            var result = await Queries.QueryAsync(
                new DocumentQuery(
                        ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                        ElsaRuntimeStorageManifest.PagePinnedExecutableArtifactIdsQuery,
                        [
                            DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                                ElsaRuntimeStorageManifest.CollectionField,
                                ElsaRuntimeStorageManifest.WorkflowExecutionStateCollection))
                        ],
                        [
                            new DocumentQueryOrder(
                                ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField),
                            new DocumentQueryOrder(
                                ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField)
                        ],
                        skip: skip,
                        take: RuntimeStorePageRequest.MaximumLimit)
                    .LatestPerKey(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField),
                cancellationToken);
            artifactIds.AddRange(result.Documents
                .Select(ReadHistoryArtifactId));
            if (result.Documents.Count < RuntimeStorePageRequest.MaximumLimit)
                return artifactIds;
        }
    }

    private static string ReadHistoryArtifactId(DocumentEnvelope envelope)
    {
        using var content = JsonDocument.Parse(envelope.ContentJson);
        if (!content.RootElement.TryGetProperty("historyArtifactId", out var artifactId) ||
            artifactId.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(artifactId.GetString()))
        {
            throw new InvalidOperationException(
                $"Workflow execution '{envelope.Id}' has no projected pinned executable artifact identity.");
        }

        return artifactId.GetString()!;
    }

    private static IReadOnlyList<DocumentQueryClause> BuildHistoryClauses(
        WorkflowExecutionStatePageQuery query)
    {
        var clauses = new List<DocumentQueryClause>();
        AddEqual(
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField,
            query.TenantId);
        AddEqual(
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryDefinitionIdField,
            query.DefinitionId);
        AddEqual(
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField,
            query.Status is { } status
                ? ((int)status).ToString(CultureInfo.InvariantCulture)
                : null);
        AddEqual(
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryRunKindField,
            query.RunKind is { } runKind
                ? ((int)runKind).ToString(CultureInfo.InvariantCulture)
                : null);
        AddEqual(
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryCorrelationIdField,
            query.CorrelationId);
        AddEqual(
            PhysicalDocumentFieldPaths.Id,
            query.WorkflowExecutionId);
        AddEqual(
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
            query.ArtifactId);
        if (query.From is { } from)
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.GreaterThanOrEqual(
                ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                from.UtcTicks.ToString(CultureInfo.InvariantCulture))));
        }
        if (query.To is { } to)
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                to.UtcTicks.ToString(CultureInfo.InvariantCulture))));
        }

        return clauses;

        void AddEqual(string path, string? value)
        {
            if (value is not null)
                clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value)));
        }
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var result = await DeleteDocumentAsync(workflowExecutionId, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

}

internal sealed record WorkflowExecutionStateDocument(
    string Collection,
    WorkflowExecutionState State,
    long HistorySortTicks,
    string HistoryWorkflowExecutionId,
    string? HistoryTenantId,
    string HistoryDefinitionId,
    int HistoryStatus,
    int HistoryRunKind,
    string? HistoryCorrelationId,
    string HistoryArtifactId)
{
    public static WorkflowExecutionStateDocument From(WorkflowExecutionState state) => new(
        ElsaRuntimeStorageManifest.WorkflowExecutionStateCollection,
        state,
        WorkflowExecutionStateHistory.SortTimestamp(state).UtcTicks,
        state.WorkflowExecutionId,
        state.TenantId,
        state.PinnedSource?.DefinitionId ?? state.PinnedExecutable.DefinitionId,
        (int)state.Status,
        (int)state.RunKind,
        state.CorrelationId,
        state.PinnedExecutable.ArtifactId);
}
