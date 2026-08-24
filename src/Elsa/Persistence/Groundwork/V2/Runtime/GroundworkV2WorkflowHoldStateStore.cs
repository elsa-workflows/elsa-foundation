using Elsa.Persistence.Core;
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
public sealed class GroundworkV2WorkflowHoldStateStore : IWorkflowHoldStateStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2WorkflowHoldStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind, targetName);
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
        var table = new TableId(unit.Name);
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

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException(
                          "Groundwork workflow-hold persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork workflow-hold state requires one explicit persistence scope; " +
                "global and across-scope access are refused.");
        }

        return sessions.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope(context.Scope.Value)),
            targetName);
    }

    private static WriteOutcome UpdateExisting(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing,
        string controlPlaneStateId)
    {
        var previous = GroundworkV2WorkflowHoldStateStorageConventions.Deserialize(existing.Values.Values);
        EnsureIdentity(previous, controlPlaneStateId);
        var version = existing.Version ?? throw new InvalidDataException(
            "Groundwork workflow-hold row did not return an optimistic revision.");
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic workflow-hold concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(version));
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

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork workflow-hold unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork workflow-hold query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Predicate Equal(ColumnRef column, string value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate Combine(IReadOnlyList<Predicate> predicates) => predicates.Count switch
    {
        0 => Predicate.AlwaysTrue.Instance,
        1 => predicates[0],
        _ => new Predicate.And(predicates)
    };

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or
        WriteOutcomeStatus.Updated or
        WriteOutcomeStatus.Upserted or
        WriteOutcomeStatus.Replayed;
}
