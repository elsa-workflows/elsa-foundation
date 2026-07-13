using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowExecutionStateStore"/>. Ordinary collection projections use the
/// portable document query contract. Run-history paging delegates to the active provider's bounded native
/// keyset plan so filtered requests never materialize the full collection.
/// </summary>
public sealed class GroundworkWorkflowExecutionStateStore : GroundworkDocumentStore, IWorkflowExecutionStateStore
{
    private readonly IGroundworkWorkflowExecutionStatePageQuery? _pageQuery;

    public GroundworkWorkflowExecutionStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
        : this(store, serializer, null)
    {
    }

    public GroundworkWorkflowExecutionStateStore(
        IDocumentStore store,
        IGroundworkRuntimeDocumentSerializer serializer,
        IGroundworkWorkflowExecutionStatePageQuery? pageQuery)
        : base(store, serializer, ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind)
    {
        _pageQuery = pageQuery;
    }

    public async ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);

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

    public async ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) =>
        await QueryDocumentsAsync<WorkflowExecutionStateDocument, WorkflowExecutionState>(
            ElsaRuntimeStorageManifest.ByCollectionIndex,
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
            document => document.State,
            cancellationToken);

    public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default) =>
        _pageQuery?.QueryPageAsync(query, cancellationToken)
        ?? throw new NotSupportedException("The active Groundwork provider has no bounded workflow execution history query plan.");

    public async ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await Store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                ElsaRuntimeStorageManifest.ByCollectionIndex,
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind),
            cancellationToken);

        return envelopes
            .Select(ReadPinnedExecutableArtifactId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
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
        // Historical versions still use the normal serializer so their upcaster chain remains authoritative.
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
    long HistorySortTicks)
{
    public static WorkflowExecutionStateDocument From(WorkflowExecutionState state) => new(
        ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
        state,
        WorkflowExecutionStateHistory.SortTimestamp(state).UtcTicks);
}
