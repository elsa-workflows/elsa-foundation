using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Runs bounded provider-side recovery routes over the current v2 liveness scope.</summary>
public sealed class GroundworkV2RuntimeRecoveryScanner : IRuntimeRecoveryScanner
{
    private readonly GroundworkV2RuntimeLivenessContext context;

    public GroundworkV2RuntimeRecoveryScanner(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        context = new GroundworkV2RuntimeLivenessContext(sessions, accessContextAccessor, targetName);
    }

    public ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(
        RuntimeRecoveryScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = context.Open();
        var states = new Dictionary<string, ExecutionLivenessState>(StringComparer.Ordinal);
        foreach (var route in Routes(request, context.Unit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = rows.Query(new QueryRequest(
                new TableId(context.Unit.Name),
                route.Where,
                [.. route.Order],
                Projection.All,
                Paging.Keyset(request.Limit)));
            foreach (var values in result.Rows)
            {
                var state = GroundworkV2RuntimeLivenessCodec.Deserialize(values);
                states[GroundworkV2RuntimeLivenessCodec.Identity(state.WorkflowExecutionId, state.OperationalStateId)] = state;
            }
        }

        return ValueTask.FromResult<IReadOnlyCollection<RuntimeRecoveryCandidate>>(
            RuntimeRecoveryCandidateSelector.Select(states.Values, request));
    }

    private static IReadOnlyList<RecoveryRoute> Routes(RuntimeRecoveryScanRequest request, StorageUnit unit)
    {
        var table = new TableId(unit.Name);
        var status = Column(table, ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField, QueryType.Int32, true);
        var interruptedAt = Column(table, ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField, QueryType.DateTimeOffset, true);
        var leaseOwner = Column(table, ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField, QueryType.String, true, ElsaRuntimeV2StorageManifest.IdMaximumLength);
        var leaseAcquiredAt = Column(table, ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField, QueryType.DateTimeOffset, true);
        var leaseExpiresAt = Column(table, ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField, QueryType.DateTimeOffset, true);
        var heartbeatOwner = Column(table, ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField, QueryType.String, true, ElsaRuntimeV2StorageManifest.IdMaximumLength);
        var heartbeatRecordedAt = Column(table, ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField, QueryType.DateTimeOffset, true);
        var hasOwner = Column(table, ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField, QueryType.Boolean, true);
        var detected = (int)RuntimeInterruptionStatus.Detected;

        if (request.OwnerId is null)
        {
            return
            [
                Route(Equal(status, detected), Order(interruptedAt)),
                Route(Due(leaseExpiresAt, request.Now), Order(leaseExpiresAt)),
                Route(Due(leaseAcquiredAt, request.Now.Subtract(request.LeaseTimeout)), Order(leaseAcquiredAt)),
                Route(Due(heartbeatRecordedAt, request.Now.Subtract(request.HeartbeatTimeout)), Order(heartbeatRecordedAt))
            ];
        }

        var owner = request.OwnerId;
        return
        [
            Route(And(Equal(status, detected), Equal(leaseOwner, owner)), Order(interruptedAt)),
            Route(And(Equal(status, detected), Equal(heartbeatOwner, owner)), Order(interruptedAt)),
            Route(And(Equal(status, detected), Equal(hasOwner, false)), Order(interruptedAt)),
            Route(And(Equal(leaseOwner, owner), Due(leaseExpiresAt, request.Now)), Order(leaseExpiresAt)),
            Route(And(Equal(leaseOwner, owner), Due(leaseAcquiredAt, request.Now.Subtract(request.LeaseTimeout))), Order(leaseAcquiredAt)),
            Route(And(Equal(heartbeatOwner, owner), Due(heartbeatRecordedAt, request.Now.Subtract(request.HeartbeatTimeout))), Order(heartbeatRecordedAt))
        ];
    }

    private static RecoveryRoute Route(Predicate where, IReadOnlyList<OrderTerm> order) => new(where, order);

    private static IReadOnlyList<OrderTerm> Order(params ColumnRef[] columns) =>
        columns.Select(column => new OrderTerm(column, OrderDirection.Ascending, NullOrder.Last)).ToArray();

    private static Predicate And(params Predicate[] predicates) => new Predicate.And(predicates);

    private static Predicate Equal(ColumnRef column, object value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate Due(ColumnRef column, DateTimeOffset value) =>
        new Predicate.Range(column, null, Bound.Inclusive(QueryConstant.Of(column, value)));

    private static ColumnRef Column(TableId table, string name, QueryType type, bool nullable, int? maxLength = null) =>
        new(table, name, type, nullable, maxLength);

    private sealed record RecoveryRoute(Predicate Where, IReadOnlyList<OrderTerm> Order);
}
