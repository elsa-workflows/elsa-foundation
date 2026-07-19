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
    {
        var result = await QueryAllAsync(cancellationToken);
        return result.Documents
            .Select(Serializer.Deserialize<WorkflowExecutionStateDocument>)
            .Select(document => document.State)
            .ToArray();
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
        var result = await QueryAllAsync(cancellationToken);

        return result.Documents
            .Select(ReadPinnedExecutableArtifactId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private Task<DocumentQueryResult> QueryAllAsync(CancellationToken cancellationToken) =>
        Queries.QueryAsync(
            new DocumentQuery(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                ElsaRuntimeStorageManifest.ListWorkflowExecutionsQuery,
                [
                    DocumentQueryClause.Of(
                        DocumentQueryComparison.Equal(
                            ElsaRuntimeStorageManifest.CollectionField,
                            ElsaRuntimeStorageManifest.WorkflowExecutionStateCollection))
                ]),
            cancellationToken);

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

    private string ReadPinnedExecutableArtifactId(DocumentEnvelope envelope)
    {
        // Current documents all have the pinned identity at this stable JSON path. Reading only that
        // element avoids materializing the much larger workflow execution state during every GC sweep.
        // A non-current envelope falls through to the serializer so version policy rejects it consistently.
        if (Serializer.IsCurrentVersion(envelope))
        {
            using var content = JsonDocument.Parse(envelope.ContentJson);
            if (content.RootElement.TryGetProperty("state", out var state) &&
                state.TryGetProperty("pinnedExecutable", out var pinnedExecutable) &&
                pinnedExecutable.TryGetProperty("artifactId", out var artifactId) &&
                artifactId.ValueKind is not JsonValueKind.Null)
                return Serializer.DeserializeElement<string>(artifactId);
        }

        return Serializer.Deserialize<WorkflowExecutionStateDocument>(envelope).State.PinnedExecutable.ArtifactId;
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
