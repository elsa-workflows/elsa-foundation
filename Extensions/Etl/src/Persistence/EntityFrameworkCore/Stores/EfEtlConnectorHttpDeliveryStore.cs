using System.Data;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    public ValueTask<EtlConnectorHttpDeliveryResult> BeginConnectorHttpDeliveryAsync(EtlConnectorPartitionLease lease,
        string batchId, EtlOutputRoute route, string requestId, CancellationToken cancellationToken = default) =>
        MutateConnectorHttpDeliveryAsync(lease, batchId, route,
            (state, effect, policy, now) => EtlHttpDeliveryTransitions.Begin(state, effect, requestId, lease.Fence, now, policy), cancellationToken);

    public ValueTask<EtlConnectorHttpDeliveryResult> CompleteConnectorHttpDeliveryAsync(EtlConnectorPartitionLease lease,
        string batchId, EtlOutputRoute route, EtlHttpDeliveryAttempt attempt, EtlHttpDeliveryResponse response,
        CancellationToken cancellationToken = default) =>
        MutateConnectorHttpDeliveryAsync(lease, batchId, route,
            (state, _, policy, now) => state is null ? new(EtlStoreStatus.Conflict, null, null) :
                EtlHttpDeliveryTransitions.Complete(state, attempt, response, lease.Fence, now, policy), cancellationToken);

    private async ValueTask<EtlConnectorHttpDeliveryResult> MutateConnectorHttpDeliveryAsync(EtlConnectorPartitionLease lease,
        string batchId, EtlOutputRoute route,
        Func<EtlConnectorHttpDelivery?, string, EtlHttpPolicy, DateTimeOffset, EtlConnectorHttpDeliveryResult> transition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!Enum.IsDefined(route)) return new(EtlStoreStatus.Invalid, null, null);
        db.ChangeTracker.Clear();
        var key = RunKey(lease.Scope, lease.RunId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        if (run is null) return new(EtlStoreStatus.NotFound, null, null);
        if (run.Status >= (int)EtlRunStatus.Succeeded) return new(EtlStoreStatus.Terminal, null, null);
        var connector = await ConnectorRowAsync(key, cancellationToken);
        if (connector is null) return new(EtlStoreStatus.Invalid, null, null);
        var partition = await db.ConnectorPartitions.SingleOrDefaultAsync(row => row.ScopeKey == key.ScopeKey &&
            row.RunKey == key.RunKey && row.PartitionOrdinal == lease.PartitionOrdinal, cancellationToken);
        if (partition is null) return new(EtlStoreStatus.NotFound, null, null);
        if (run.Status != (int)EtlRunStatus.Running || !HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks()))
            return new(EtlStoreStatus.LeaseLost, null, null);
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
        var state = await ReadConnectorStateAsync(invocation, connector, cancellationToken);
        var position = await ReadConnectorPositionAsync(partition, state.Pipeline, state.Bindings, state.Prepared, cancellationToken);
        if (position != lease.Position || partition.OwedBatchId != batchId) return new(EtlStoreStatus.Conflict, null, null);
        var owed = await ReadConnectorOwedBatchAsync(partition, new(invocation, state.Bindings), state.Pipeline,
            state.Prepared!, position, cancellationToken);
        if (owed?.Output is not { } output) return new(EtlStoreStatus.Conflict, null, null);
        var routeOutput = route == EtlOutputRoute.Main ? output.Main : output.Errors;
        if (routeOutput?.Kind != EtlConnectorKind.Http) return new(EtlStoreStatus.Invalid, null, null);
        var deliveries = owed.HttpDeliveries ?? new(null, null);
        var previous = route == EtlOutputRoute.Main ? deliveries.Main : deliveries.Errors;
        var result = transition(previous, routeOutput.EffectId, state.Pipeline.Http, EtlRuntimeRecordCodec.ReadUtc(NowTicks()));
        if (!HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks())) return new(EtlStoreStatus.LeaseLost, null, null);
        if (result.Status != EtlStoreStatus.Applied)
            return result.Permit is { } replayPermit && replayPermit.ExpiresAt.UtcTicks <= NowTicks()
                ? new(EtlStoreStatus.Conflict, result.Delivery, null) : result;
        if (result.Delivery is null) throw new InvalidDataException("An applied HTTP transition omitted its durable state.");
        deliveries = route == EtlOutputRoute.Main ? deliveries with { Main = result.Delivery } : deliveries with { Errors = result.Delivery };
        var batch = await db.ConnectorBatches.SingleAsync(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey &&
            row.PartitionOrdinal == lease.PartitionOrdinal && row.BatchId == batchId, cancellationToken);
        string json;
        try { json = EtlConnectorRecordCodec.Write(deliveries); }
        catch (ArgumentException) { return new(EtlStoreStatus.BudgetExceeded, previous, null); }
        var retainedBytes = checked(MetadataBytes(json) + HttpDeliveryTransitionReserve(deliveries));
        if (retainedBytes > EtlConnectorRecordCodec.MaximumDocumentBytes)
            return new(EtlStoreStatus.BudgetExceeded, previous, null);
        var delta = checked(retainedBytes - HttpDeliveryMetadataBytes(batch.HttpDeliveryJson));
        if (delta > state.Pipeline.Http.MaxRetainedMetadataBytes - connector.RetainedMetadataBytes)
            return new(EtlStoreStatus.BudgetExceeded, previous, null);
        batch.HttpDeliveryJson = json;
        batch.HttpDeliveryDigest = MetadataDigest(json);
        batch.MetadataBytes = checked(batch.MetadataBytes + delta);
        partition.BatchMetadataBytes = checked(partition.BatchMetadataBytes + delta);
        partition.MetadataBytes = checked(partition.MetadataBytes + delta);
        partition.NotBeforeUtcTicks = ConnectorNotBefore(output, deliveries);
        partition.Revision = checked(partition.Revision + 1);
        connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes + delta);
        connector.Revision = checked(connector.Revision + 1);
        run.UpdatedAtUtcTicks = NowTicks();
        run.Revision = checked(run.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (!HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks())) return new(EtlStoreStatus.LeaseLost, null, null);
        if (result.Permit is { } permit && permit.ExpiresAt.UtcTicks <= NowTicks()) return new(EtlStoreStatus.Conflict, null, null);
        await tx.CommitAsync(cancellationToken);
        return result;
    }

    private static EtlConnectorHttpDeliveries? ReadConnectorHttpDeliveries(EtlConnectorBatchRecord batch,
        EtlConnectorBatchOutput? output, EtlHttpPolicy policy)
    {
        ValidateHttpDeliveryEnvelope(batch);
        if (batch.HttpDeliveryJson is null) return null;
        if (output is null) throw new InvalidDataException("HTTP delivery has no retained output plan.");
        var deliveries = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(batch.HttpDeliveryJson);
        if (deliveries.Main is null && deliveries.Errors is null) throw new InvalidDataException("HTTP delivery history is empty.");
        return new(ValidateRoute(deliveries.Main, output.Main), ValidateRoute(deliveries.Errors, output.Errors));

        EtlConnectorHttpDelivery? ValidateRoute(EtlConnectorHttpDelivery? delivery, EtlConnectorRouteOutput? route)
        {
            if (delivery is null) return null;
            if (route?.Kind != EtlConnectorKind.Http) throw new InvalidDataException("Delivery history has no HTTP route.");
            EtlHttpDeliveryTransitions.Validate(delivery, route.EffectId, policy);
            return delivery with { Attempts = Array.AsReadOnly(delivery.Attempts.ToArray()) };
        }
    }

    // Reserve enough canonical JSON growth to record the bounded outcome before sending.
    // InFlight -> Waiting can add a status and three UTC timestamps; Waiting -> Failed can
    // change the update timestamp. Each releases at least 256 bytes, more than that growth.
    // A new attempt pays its history growth plus the replenished reserve before dispatch.
    private static long HttpDeliveryTransitionReserve(EtlConnectorHttpDeliveries deliveries) =>
        Reserve(deliveries.Main) + Reserve(deliveries.Errors);

    private static long Reserve(EtlConnectorHttpDelivery? delivery) => delivery?.Phase switch
    {
        null or EtlHttpDeliveryPhase.Acknowledged or EtlHttpDeliveryPhase.Failed => 0,
        EtlHttpDeliveryPhase.Waiting => 256,
        EtlHttpDeliveryPhase.InFlight => 512,
        _ => throw new InvalidDataException("The HTTP delivery phase is invalid.")
    };

    private static long HttpDeliveryMetadataBytes(string? json) => json is null ? 0 :
        checked(MetadataBytes(json) + HttpDeliveryTransitionReserve(EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(json)));

    private static void ValidateHttpDeliveryEnvelope(EtlConnectorBatchRecord batch)
    {
        if (batch.HttpDeliveryJson is null ? batch.HttpDeliveryDigest is not null :
            batch.OutputJson is null || batch.HttpDeliveryDigest != MetadataDigest(batch.HttpDeliveryJson))
            throw new InvalidDataException("The HTTP delivery metadata is inconsistent.");
    }

    private static long? ConnectorNotBefore(EtlConnectorBatchOutput? output, EtlConnectorHttpDeliveries? deliveries)
    {
        if (output is null) return null;
        long? earliest = null;
        foreach (var (route, delivery) in new[] { (output.Main, deliveries?.Main), (output.Errors, deliveries?.Errors) })
        {
            if (route is null || delivery?.Phase == EtlHttpDeliveryPhase.Acknowledged) continue;
            // File publication, unstarted HTTP work, uncertain in-flight work, and failure settlement
            // remain eligible. Only a partition whose remaining work is all delayed may be deferred.
            if (route.Kind != EtlConnectorKind.Http || delivery?.Phase != EtlHttpDeliveryPhase.Waiting) return null;
            var due = delivery.NotBefore?.UtcTicks ?? throw new InvalidDataException("Waiting delivery has no retry time.");
            earliest = earliest is null ? due : Math.Min(earliest.Value, due);
        }
        return earliest;
    }
}
