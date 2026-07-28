using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IExecutionLivenessStateStore"/>. The document is wrapped in an envelope that
/// stamps both a top-level <c>workflowExecutionId</c> (for the per-workflow list) and a constant
/// collection partition (for the unfiltered <see cref="ListAllAsync"/>), so both lists run through the
/// declared-index equality query every provider supports.
/// </summary>
public sealed class GroundworkExecutionLivenessStateStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind, boundedStore), IExecutionLivenessStateStore
{
    public async ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.OperationalStateId);

        await SaveDocumentAsync(
            DocumentIdentity(state.WorkflowExecutionId, state.OperationalStateId),
            NewDocument(state),
            cancellationToken);

        return state;
    }

    public async ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(
        ExecutionLivenessState state,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        if (expectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        var document = NewDocument(state);
        var result = await SaveDocumentAsync(
            DocumentIdentity(state.WorkflowExecutionId, state.OperationalStateId),
            document,
            cancellationToken,
            expectedRevision);

        return new ExecutionLivenessStateWriteResult(result.Status switch
        {
            DocumentStoreWriteStatus.Saved => ExecutionLivenessStateWriteStatus.Saved,
            DocumentStoreWriteStatus.NotFound => ExecutionLivenessStateWriteStatus.NotFound,
            DocumentStoreWriteStatus.ConcurrencyConflict => ExecutionLivenessStateWriteStatus.RevisionConflict,
            _ => throw new InvalidOperationException(
                $"Groundwork rejected liveness-state write with unsupported status '{result.Status}'.")
        }, result.Document?.Version);
    }

    public async ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);

        return await LoadDocumentAsync<ExecutionLivenessStateDocument, ExecutionLivenessState>(
            DocumentId.Compose(workflowExecutionId, operationalStateId), document => document.State, cancellationToken);
    }

    public async ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(
        string workflowExecutionId,
        string operationalStateId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, operationalStateId);
        var envelope = await Store.LoadAsync(DocumentKind, DocumentIdentity(workflowExecutionId, operationalStateId), cancellationToken);
        if (envelope is null)
            return null;

        var document = Serializer.Deserialize<ExecutionLivenessStateDocument>(envelope);
        EnsureIdentity(document.State, workflowExecutionId, operationalStateId);
        return new VersionedExecutionLivenessState(document.State, envelope.Version);
    }

    public async ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListPageAsync(
        ExecutionLivenessStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.PageExecutionLivenessStatesByWorkflowExecutionQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, query.WorkflowExecutionId)],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField)],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);

        return new RuntimeStorePage<ExecutionLivenessState>(
            query,
            result.Documents
                .Select(envelope => Serializer.Deserialize<ExecutionLivenessStateDocument>(envelope).State)
                .ToArray(),
            result.NextContinuation);
    }

    public async ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListAllPageAsync(
        RuntimeStorePageRequest query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.PageExecutionLivenessStatesQuery,
                [Equal(
                    ElsaRuntimeStorageManifest.CollectionField,
                    ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind)],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField)
                ],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);

        return new RuntimeStorePage<ExecutionLivenessState>(
            query,
            result.Documents
                .Select(envelope => Serializer.Deserialize<ExecutionLivenessStateDocument>(envelope).State)
                .ToArray(),
            result.NextContinuation);
    }

    public async ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default) =>
        await RuntimeOperationalStorePagingExtensions.ListAllAsync(this, workflowExecutionId, cancellationToken);

    public async ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        await RuntimeOperationalStorePagingExtensions.ListAllAsync(this, cancellationToken);

    internal static string DocumentIdentity(string workflowExecutionId, string operationalStateId) =>
        DocumentId.Compose(workflowExecutionId, operationalStateId);

    internal static string OwnershipStateId(string workflowExecutionId) => $"ownership:{workflowExecutionId}";

    private static ExecutionLivenessStateDocument NewDocument(ExecutionLivenessState state) =>
        new(
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            state.WorkflowExecutionId,
            state.ExecutionLease is not null || state.Heartbeat is not null,
            state);

    private static void ValidateState(ExecutionLivenessState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateIdentity(state.WorkflowExecutionId, state.OperationalStateId);
    }

    private static void ValidateIdentity(string workflowExecutionId, string operationalStateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);
    }

    private static void EnsureIdentity(
        ExecutionLivenessState state,
        string workflowExecutionId,
        string operationalStateId)
    {
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(state.OperationalStateId, operationalStateId))
        {
            throw new InvalidOperationException("Groundwork liveness-state document identity does not match its content.");
        }
    }

    private static DocumentQueryClause Equal(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value));

    internal sealed record ExecutionLivenessStateDocument(
        string Collection,
        string WorkflowExecutionId,
        bool HasOperationalOwner,
        ExecutionLivenessState State);
}
