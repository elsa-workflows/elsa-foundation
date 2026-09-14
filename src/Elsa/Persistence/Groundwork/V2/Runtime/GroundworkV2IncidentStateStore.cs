using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 incident-state store.</summary>
/// <remarks>
/// Incident identities are composite workflow-execution/incident identities. All reads require one
/// explicit persistence scope and all collection reads use provider-owned bounded keyset pages.
/// </remarks>
public sealed class GroundworkV2IncidentStateStore : GroundworkV2RuntimeStoreBase, IIncidentStateStore
{
    public GroundworkV2IncidentStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
        : base(sessions, accessContextAccessor, targetName, "incident state", ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind)
    {
    }

    public ValueTask<bool> TryAddAsync(
        IncidentState state,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2IncidentStateStorageConventions.Validate(state);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2IncidentStateStorageConventions.PhysicalId(
                state.WorkflowExecutionId,
                state.IncidentId));
        if (session.Read(key) is { } existing)
        {
            EnsureIdentity(existing, state.WorkflowExecutionId, state.IncidentId);
            return ValueTask.FromResult(false);
        }

        var result = session.Insert(
            GroundworkV2IncidentStateStorageConventions.Values(state),
            WriteOptions.CreateOnly);
        if (result.Status == WriteOutcomeStatus.Inserted)
            return ValueTask.FromResult(true);
        if (result.Status == WriteOutcomeStatus.ConcurrencyConflict)
        {
            var winner = session.Read(key) ?? throw new InvalidOperationException(
                $"Incident '{state.IncidentId}' conflicted during creation but could not be reloaded.");
            EnsureIdentity(winner, state.WorkflowExecutionId, state.IncidentId);
            return ValueTask.FromResult(false);
        }

        throw new InvalidOperationException(
            $"Groundwork rejected incident '{state.IncidentId}' with status '{result.Status}'.");
    }

    public ValueTask<IncidentState> SaveAsync(
        IncidentState state,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2IncidentStateStorageConventions.Validate(state);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var physicalId = GroundworkV2IncidentStateStorageConventions.PhysicalId(
            state.WorkflowExecutionId,
            state.IncidentId);
        var key = GroundworkRuntimeRowStore.Key(physicalId);
        var values = GroundworkV2IncidentStateStorageConventions.Values(state);
        var result = session.Read(key) is { } existing
            ? UpdateExisting(session, values, existing, state)
            : session.Insert(values, WriteOptions.CreateOnly);

        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                "Groundwork incident-state save lost a concurrent write; retry the operation.");
        }

        return ValueTask.FromResult(state);
    }

    public ValueTask<IncidentState?> FindAsync(
        string workflowExecutionId,
        string incidentId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, incidentId);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Open().Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2IncidentStateStorageConventions.PhysicalId(workflowExecutionId, incidentId)));
        if (entry is null)
            return ValueTask.FromResult<IncidentState?>(null);

        var state = GroundworkV2IncidentStateStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(state, workflowExecutionId, incidentId);
        return ValueTask.FromResult<IncidentState?>(state);
    }

    public ValueTask<int> CountAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var table = new TableId(Unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var incident = Column(table, ElsaRuntimeV2StorageManifest.IncidentIdField);
        var result = Open().Query(new QueryRequest(
            table,
            Equal(workflow, workflowExecutionId),
            [new OrderTerm(incident, OrderDirection.Ascending, NullOrder.Last)],
            Projection.ColumnsOnly(incident),
            Paging.Keyset(1),
            ResultShape.TotalCount.Instance));
        var count = result.TotalCount ?? throw new InvalidDataException(
            "Groundwork incident-state count did not return its provider-side total.");
        return ValueTask.FromResult(count > int.MaxValue ? int.MaxValue : (int)count);
    }

    public ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<IncidentState>>(
            QueryAll(workflowExecutionId, status: null, cancellationToken));
    }

    public ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<IncidentState>>(
            QueryAll(workflowExecutionId, IncidentStatus.Blocking, cancellationToken));
    }

    private IReadOnlyCollection<IncidentState> QueryAll(
        string workflowExecutionId,
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        var session = Open();
        var table = new TableId(Unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var incident = Column(table, ElsaRuntimeV2StorageManifest.IncidentIdField);
        var predicates = new List<Predicate> { Equal(workflow, workflowExecutionId) };
        if (status is { } filterStatus)
        {
            var statusColumn = Column(table, ElsaRuntimeV2StorageManifest.StatusField);
            predicates.Add(Equal(statusColumn, filterStatus.ToString()));
        }

        var rows = new List<IncidentState>();
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                table,
                Combine(predicates),
                [new OrderTerm(incident, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                PagingFor(RuntimeStorePageRequest.MaximumLimit, cursor)));
            rows.AddRange(result.Rows.Select(GroundworkV2IncidentStateStorageConventions.Deserialize));
            if (result.NextContinuationToken is { } next && !seenContinuations.Add(next))
            {
                throw new InvalidDataException(
                    "Groundwork incident-state continuation repeated or cycled.");
            }

            cursor = result.NextContinuationToken;
        } while (cursor is not null);

        foreach (var state in rows)
        {
            EnsureIdentity(state, workflowExecutionId, state.IncidentId);
            if (status.HasValue && state.Status != status.Value)
            {
                throw new InvalidDataException(
                    "Groundwork incident-state status projection does not match its current content.");
            }
        }

        return rows;
    }

    private WriteOutcome UpdateExisting(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing,
        IncidentState state)
    {
        var previous = GroundworkV2IncidentStateStorageConventions.Deserialize(existing.Values.Values);
        EnsureIdentity(previous, state.WorkflowExecutionId, state.IncidentId);
        IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(previous, state);
        var revision = existing.Version ?? throw new InvalidDataException(
            $"Groundwork incident '{state.IncidentId}' did not return an optimistic revision.");
        return ConditionalUpsert(session, values, revision);
    }

    private static void ValidateIdentity(string workflowExecutionId, string incidentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        _ = GroundworkV2IncidentStateStorageConventions.PhysicalId(workflowExecutionId, incidentId);
    }

    private static void EnsureIdentity(
        StoredEntry existing,
        string workflowExecutionId,
        string incidentId) =>
        EnsureIdentity(
            GroundworkV2IncidentStateStorageConventions.Deserialize(existing.Values.Values),
            workflowExecutionId,
            incidentId);

    private static void EnsureIdentity(
        IncidentState state,
        string workflowExecutionId,
        string incidentId)
    {
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(state.IncidentId, incidentId))
        {
            throw new InvalidDataException(
                "Groundwork incident-state row identity does not match its requested key.");
        }
    }
}
