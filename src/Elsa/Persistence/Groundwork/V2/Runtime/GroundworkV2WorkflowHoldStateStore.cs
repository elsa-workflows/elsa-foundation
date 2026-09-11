using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 workflow-hold state store.</summary>
/// <remarks>
/// Each control-plane state has one stable row identity. Reads require one explicit persistence
/// scope, while workflow and global enumeration are provider-owned bounded keyset queries.
/// </remarks>
public sealed class GroundworkV2WorkflowHoldStateStore : GroundworkV2RuntimeStoreBase, IWorkflowHoldStateStore
{

    public GroundworkV2WorkflowHoldStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
        : base(sessions, accessContextAccessor, targetName, "workflow-hold state", ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind)
    {
    }

    public ValueTask<WorkflowHoldState> SaveAsync(
        WorkflowHoldState state,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2WorkflowHoldStateStorageConventions.Validate(state);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var values = GroundworkV2WorkflowHoldStateStorageConventions.Values(state);
        var key = GroundworkRuntimeRowStore.Key(state.ControlPlaneStateId);
        var result = session.Read(key) is { } existing
            ? UpdateExisting(session, values, existing, state.ControlPlaneStateId)
            : session.Insert(values, WriteOptions.CreateOnly);

        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                "Groundwork workflow-hold save lost a concurrent write; retry the operation.");
        }

        return ValueTask.FromResult(state);
    }

    public ValueTask<WorkflowHoldState?> FindAsync(
        string controlPlaneStateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneStateId);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Open().Read(GroundworkRuntimeRowStore.Key(controlPlaneStateId));
        if (entry is null)
            return ValueTask.FromResult<WorkflowHoldState?>(null);

        var state = GroundworkV2WorkflowHoldStateStorageConventions.Deserialize(entry.Values.Values);
        EnsureIdentity(state, controlPlaneStateId);
        return ValueTask.FromResult<WorkflowHoldState?>(state);
    }

    public ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListForWorkflowExecutionAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<WorkflowHoldState>>(
            QueryAll(workflowExecutionId, cancellationToken));
    }

    public ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<WorkflowHoldState>>(
            QueryAll(null, cancellationToken));
    }

    private IReadOnlyCollection<WorkflowHoldState> QueryAll(
        string? workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var session = Open();
        var table = new TableId(Unit.Name);
        var collection = Column(table, ElsaRuntimeV2StorageManifest.CollectionField);
        var predicates = new List<Predicate>
        {
            Equal(collection, ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind)
        };
        if (workflowExecutionId is not null)
        {
            predicates.Add(Equal(
                Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField),
                workflowExecutionId));
        }

        var id = Column(table, ElsaRuntimeV2StorageManifest.IdField);
        var rows = new List<WorkflowHoldState>();
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        string? continuationToken = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                table,
                Combine(predicates),
                [new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                PagingFor(RuntimeStorePageRequest.MaximumLimit, continuationToken)));
            rows.AddRange(result.Rows.Select(values =>
            {
                var state = GroundworkV2WorkflowHoldStateStorageConventions.Deserialize(values);
                if (workflowExecutionId is not null)
                    EnsureWorkflowIdentity(state, workflowExecutionId);
                return state;
            }));

            if (result.NextContinuationToken is { } next && !seenContinuations.Add(next))
            {
                throw new InvalidDataException(
                    "Groundwork workflow-hold continuation repeated or cycled.");
            }

            continuationToken = result.NextContinuationToken;
        } while (continuationToken is not null);

        return rows;
    }

    private WriteOutcome UpdateExisting(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing,
        string controlPlaneStateId)
    {
        var previous = GroundworkV2WorkflowHoldStateStorageConventions.Deserialize(existing.Values.Values);
        EnsureIdentity(previous, controlPlaneStateId);
        var version = existing.Version ?? throw new InvalidDataException(
            "Groundwork workflow-hold row did not return an optimistic revision.");
        return ConditionalUpsert(session, values, version);
    }

    private static void EnsureIdentity(
        WorkflowHoldState state,
        string controlPlaneStateId)
    {
        if (!StringComparer.Ordinal.Equals(state.ControlPlaneStateId, controlPlaneStateId))
        {
            throw new InvalidDataException(
                "Groundwork workflow-hold row identity does not match its requested key.");
        }
    }

    private static void EnsureWorkflowIdentity(
        WorkflowHoldState state,
        string workflowExecutionId)
    {
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId))
        {
            throw new InvalidDataException(
                "Groundwork workflow-hold row workflow identity does not match its requested query.");
        }
    }
}
