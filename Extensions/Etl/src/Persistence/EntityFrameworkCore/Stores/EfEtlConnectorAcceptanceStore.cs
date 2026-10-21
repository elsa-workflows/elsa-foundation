using System.Data;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    public async ValueTask<EtlStoreStatus> RecordConnectorFilePublicationAsync(EtlConnectorPartitionLease lease,
        string batchId, EtlOutputRoute route, EtlObjectReference publication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!Enum.IsDefined(route) || publication is null) return EtlStoreStatus.Invalid;
        db.ChangeTracker.Clear();
        var key = RunKey(lease.Scope, lease.RunId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        if (run is null) return EtlStoreStatus.NotFound;
        if (run.Status >= (int)EtlRunStatus.Succeeded) return EtlStoreStatus.Terminal;
        var connector = await ConnectorRowAsync(key, cancellationToken);
        if (connector is null) return EtlStoreStatus.Invalid;
        var partition = await db.ConnectorPartitions.SingleOrDefaultAsync(row => row.ScopeKey == key.ScopeKey &&
            row.RunKey == key.RunKey && row.PartitionOrdinal == lease.PartitionOrdinal, cancellationToken);
        if (partition is null) return EtlStoreStatus.NotFound;
        if (run.Status != (int)EtlRunStatus.Running || !HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks()))
            return EtlStoreStatus.LeaseLost;
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
        var state = await ReadConnectorStateAsync(invocation, connector, cancellationToken);
        var position = await ReadConnectorPositionAsync(partition, state.Pipeline, state.Bindings, state.Prepared, cancellationToken);
        if (position != lease.Position || partition.OwedBatchId != batchId) return EtlStoreStatus.Conflict;
        var owed = await ReadConnectorOwedBatchAsync(partition, new(invocation, state.Bindings), state.Pipeline,
            state.Prepared!, position, cancellationToken);
        if (owed?.Output is not { } output) return EtlStoreStatus.Conflict;
        var planned = route == EtlOutputRoute.Main ? output.Main : output.Errors;
        if (planned?.Kind != EtlConnectorKind.Jsonl) return EtlStoreStatus.Invalid;
        var expected = planned.Body with { Key = EtlRuntimeIdentity.ConnectorFileOutputKey(planned.EffectId) };
        if (publication != expected) return EtlStoreStatus.Conflict;
        if (!HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks())) return EtlStoreStatus.LeaseLost;
        if (route == EtlOutputRoute.Main ? owed.FilePublications.Main : owed.FilePublications.Errors) return EtlStoreStatus.Replay;
        var batch = await db.ConnectorBatches.SingleAsync(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey &&
            row.PartitionOrdinal == lease.PartitionOrdinal && row.BatchId == batchId, cancellationToken);
        if (route == EtlOutputRoute.Main) batch.MainFilePublished = true;
        else batch.ErrorFilePublished = true;
        partition.NotBeforeUtcTicks = ConnectorNotBefore(output, owed.HttpDeliveries, ReadConnectorFilePublications(batch, output));
        partition.Revision = checked(partition.Revision + 1);
        run.UpdatedAtUtcTicks = NowTicks();
        run.Revision = checked(run.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (!HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks())) return EtlStoreStatus.LeaseLost;
        await tx.CommitAsync(cancellationToken);
        return EtlStoreStatus.Applied;
    }

    public async ValueTask<EtlConnectorPositionResult> AcceptConnectorBatchAsync(EtlConnectorPartitionLease lease,
        string batchId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        db.ChangeTracker.Clear();
        var key = RunKey(lease.Scope, lease.RunId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        if (run is null) return new(EtlStoreStatus.NotFound, null);
        if (run.Status >= (int)EtlRunStatus.Succeeded) return new(EtlStoreStatus.Terminal, null);
        var connector = await ConnectorRowAsync(key, cancellationToken);
        if (connector is null) return new(EtlStoreStatus.Invalid, null);
        var partition = await db.ConnectorPartitions.SingleOrDefaultAsync(row => row.ScopeKey == key.ScopeKey &&
            row.RunKey == key.RunKey && row.PartitionOrdinal == lease.PartitionOrdinal, cancellationToken);
        if (partition is null) return new(EtlStoreStatus.NotFound, null);
        if (run.Status != (int)EtlRunStatus.Running || !HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks()))
            return new(EtlStoreStatus.LeaseLost, null);
        var expiry = partition.ExpiresAtUtcTicks!.Value;
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
        var state = await ReadConnectorStateAsync(invocation, connector, cancellationToken);
        var batch = await db.ConnectorBatches.SingleOrDefaultAsync(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey &&
            row.PartitionOrdinal == lease.PartitionOrdinal && row.BatchId == batchId, cancellationToken);
        if (batch is null) return new(EtlStoreStatus.Conflict, null);
        var position = await ReadConnectorPositionAsync(partition, state.Pipeline, state.Bindings, state.Prepared,
            cancellationToken);
        if (batch.Settled)
        {
            await ReadAcceptedConnectorBatchAsync(partition, batch, new(invocation, state.Bindings), state.Pipeline, state.Prepared!, position, cancellationToken);
            var currentOwed = await ReadConnectorOwedBatchAsync(partition, new(invocation, state.Bindings), state.Pipeline,
                state.Prepared!, position, cancellationToken);
            return PositionResult(EtlStoreStatus.Replay, lease with { OwedBatch = currentOwed }, partition, position);
        }
        if (position != lease.Position || partition.OwedBatchId != batchId) return new(EtlStoreStatus.Conflict, null);
        var owed = await ReadConnectorOwedBatchAsync(partition, new(invocation, state.Bindings), state.Pipeline,
            state.Prepared!, position, cancellationToken);
        if (owed?.Output is not { } output || !ConnectorOutputIsDelivered(output, owed.HttpDeliveries, owed.FilePublications))
            return new(EtlStoreStatus.Conflict, null);

        var next = ConnectorPositionAfterBatch(position, owed.Batch.Range);
        var addedRequest = next is EtlConnectorHttpPosition { Exhausted: false } nextHttp &&
            position is EtlConnectorHttpPosition previousHttp && nextHttp.PageSequence > previousHttp.PageSequence
                ? HttpRequestRecord(partition, nextHttp) : null;
        if (owed.Batch.Range is EtlConnectorHttpBatchRange range)
        {
            var page = await db.ConnectorHttpPages.SingleAsync(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey &&
                row.PartitionOrdinal == lease.PartitionOrdinal && row.PageSequence == range.PageSequence, cancellationToken);
            if (page.AcceptedItemCount != range.StartItem) throw new InvalidDataException("The page acceptance differs from its current input position.");
            page.AcceptedItemCount = range.EndItemExclusive;
        }
        long sourceReserveDelta = 0;
        if (position is EtlConnectorHttpPosition sourcePosition)
        {
            var sourceRequest = await HttpRequestForUpdateAsync(partition, sourcePosition.PageSequence, cancellationToken);
            if (sourceRequest.FetchJson is not null)
            {
                var remaining = next is EtlConnectorHttpPosition { Page: not null } continued &&
                    continued.PageSequence == sourcePosition.PageSequence
                    ? HttpSourceAttachedProgressMetadataReserve(continued, state.Pipeline.Http) : 0;
                sourceReserveDelta = checked(remaining - sourceRequest.SourceMetadataReserveBytes);
                if (sourceReserveDelta > 0)
                    throw new InvalidDataException("The accepted source page exhausted its reserved progress capacity.");
                sourceRequest.SourceMetadataReserveBytes = remaining;
                sourceRequest.MetadataBytes = checked(sourceRequest.MetadataBytes + sourceReserveDelta);
            }
        }
        var positionJson = EtlConnectorRecordCodec.Write<EtlConnectorPosition>(next);
        var metadataDelta = checked(MetadataBytes(positionJson) - MetadataBytes(partition.PositionJson) + (addedRequest?.MetadataBytes ?? 0) - batch.AcceptanceMetadataBytes + sourceReserveDelta);
        if (metadataDelta > state.Pipeline.Http.MaxRetainedMetadataBytes - connector.RetainedMetadataBytes)
            return new(EtlStoreStatus.BudgetExceeded, null);
        if (addedRequest is not null)
        {
            db.ConnectorHttpRequests.Add(addedRequest);
            partition.CurrentRequestSequence = addedRequest.PageSequence;
            partition.CurrentRequestHash = addedRequest.RequestHash;
        }
        partition.BatchMetadataBytes = checked(partition.BatchMetadataBytes - batch.AcceptanceMetadataBytes);
        batch.MetadataBytes = checked(batch.MetadataBytes - batch.AcceptanceMetadataBytes);
        batch.AcceptanceMetadataBytes = 0;
        batch.Settled = true;
        partition.PositionJson = positionJson;
        partition.Completed = EtlConnectorRecordCodec.Complete(next);
        partition.OwedBatchId = null;
        partition.NotBeforeUtcTicks = null;
        partition.AcceptedBatchCount = checked(partition.AcceptedBatchCount + 1);
        partition.AcceptedRecords = checked(partition.AcceptedRecords + output.AcceptedRecords);
        partition.RejectedRecords = checked(partition.RejectedRecords + output.RejectedRecords);
        partition.FilteredRecords = checked(partition.FilteredRecords + output.FilteredRecords);
        partition.MetadataBytes = checked(partition.MetadataBytes + metadataDelta);
        partition.Revision = checked(partition.Revision + 1);
        connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes + metadataDelta);
        connector.Revision = checked(connector.Revision + 1);
        run.AcceptedRecords = checked(run.AcceptedRecords + output.AcceptedRecords);
        run.RejectedRecords = checked(run.RejectedRecords + output.RejectedRecords);
        run.FilteredRecords = checked(run.FilteredRecords + output.FilteredRecords);
        run.OutstandingRecords = checked(run.OutstandingRecords - batch.RecordCount);
        run.OutstandingBytes = checked(run.OutstandingBytes - batch.InputBytes);
        run.UpdatedAtUtcTicks = NowTicks();
        run.Revision = checked(run.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        await CompleteConnectorRunIfReadyAsync(key, run, invocation, cancellationToken);
        // Progress and terminal delivery both roll back if authority expired during either save.
        if (expiry <= NowTicks() || !HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks()))
            return new(EtlStoreStatus.LeaseLost, null);
        await tx.CommitAsync(cancellationToken);
        return new(EtlStoreStatus.Applied, lease with { Position = next, OwedBatch = null, ExpiresAt = EtlRuntimeRecordCodec.ReadUtc(expiry) });
    }

    private static EtlConnectorPosition ConnectorPositionAfterBatch(EtlConnectorPosition position, EtlConnectorBatchRange range) => (position, range) switch
    {
        (EtlConnectorFilePosition file, EtlConnectorFileBatchRange batch) => file with { NextOrdinal = batch.EndOrdinalExclusive },
        (EtlConnectorHttpPosition http, EtlConnectorHttpBatchRange batch) => batch.EndItemExclusive == http.Page!.RecordCount
            ? AfterHttpPage(http) : http with { NextItem = batch.EndItemExclusive },
        _ => throw new InvalidDataException("The retained connector batch has an incompatible position.")
    };

    private static long ConnectorAcceptanceReserve(EtlConnectorPartitionRecord partition, EtlConnectorPosition position,
        EtlConnectorBatchDescriptor batch)
    {
        var next = ConnectorPositionAfterBatch(position, batch.Range);
        var requestBytes = next is EtlConnectorHttpPosition { Exhausted: false } nextHttp &&
            position is EtlConnectorHttpPosition previousHttp && nextHttp.PageSequence > previousHttp.PageSequence
                ? HttpRequestRecord(partition, nextHttp).MetadataBytes : 0;
        return Math.Max(0, checked(MetadataBytes(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(next)) - MetadataBytes(partition.PositionJson) + requestBytes));
    }

    private static long MissingAcceptanceReserve(EtlConnectorPartitionRecord partition, EtlConnectorPosition position,
        EtlConnectorBatchDescriptor descriptor, EtlConnectorBatchRecord batch) =>
        checked(ConnectorAcceptanceReserve(partition, position, descriptor) - batch.AcceptanceMetadataBytes);

    private static void RetainAcceptanceReserve(EtlConnectorRunRecord connector, EtlConnectorPartitionRecord partition,
        EtlConnectorBatchRecord batch, long delta)
    {
        if (delta < 0) throw new InvalidDataException("The connector acceptance reserve is larger than its transition.");
        batch.AcceptanceMetadataBytes = checked(batch.AcceptanceMetadataBytes + delta);
        batch.MetadataBytes = checked(batch.MetadataBytes + delta);
        partition.BatchMetadataBytes = checked(partition.BatchMetadataBytes + delta);
        partition.MetadataBytes = checked(partition.MetadataBytes + delta);
        connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes + delta);
    }

    private async ValueTask CompleteConnectorRunIfReadyAsync((string ScopeKey, string RunKey) key,
        EtlRuntimeRunRecord run, EtlInvocation invocation, CancellationToken cancellationToken)
    {
        if (run.OutstandingRecords != 0 || run.OutstandingBytes != 0 ||
            await db.ConnectorPartitions.AnyAsync(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey && !row.Completed, cancellationToken) ||
            !await ConnectorIsCompleteAsync(key, cancellationToken)) return;
        var outcome = run.RejectedRecords == 0 ? EtlTerminalOutcome.Succeeded : EtlTerminalOutcome.BusinessError;
        var diagnostic = outcome == EtlTerminalOutcome.BusinessError ? new EtlRunDiagnostic("etl.rows-rejected") : null;
        var now = NowTicks();
        AddTerminalRows(run, BuildTerminal(invocation, outcome, diagnostic, run, now), now);
        run.Status = outcome == EtlTerminalOutcome.Succeeded ? (int)EtlRunStatus.Succeeded : (int)EtlRunStatus.BusinessError;
        run.UpdatedAtUtcTicks = now;
        run.Revision = checked(run.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static EtlConnectorFilePublications ReadConnectorFilePublications(EtlConnectorBatchRecord batch, EtlConnectorBatchOutput? output)
    {
        if (batch.MainFilePublished && output?.Main?.Kind != EtlConnectorKind.Jsonl ||
            batch.ErrorFilePublished && output?.Errors?.Kind != EtlConnectorKind.Jsonl)
            throw new InvalidDataException("The connector file publication has no matching JSONL route.");
        return new(batch.MainFilePublished, batch.ErrorFilePublished);
    }

    private static bool ConnectorOutputIsDelivered(EtlConnectorBatchOutput output, EtlConnectorHttpDeliveries? deliveries,
        EtlConnectorFilePublications publications) =>
        Delivered(output.Main, deliveries?.Main, publications.Main) && Delivered(output.Errors, deliveries?.Errors, publications.Errors);

    private static bool Delivered(EtlConnectorRouteOutput? route, EtlConnectorHttpDelivery? delivery, bool published) => route?.Kind switch
    {
        null => true,
        EtlConnectorKind.Jsonl => published,
        EtlConnectorKind.Http => delivery?.Phase == EtlHttpDeliveryPhase.Acknowledged,
        _ => false
    };
}
