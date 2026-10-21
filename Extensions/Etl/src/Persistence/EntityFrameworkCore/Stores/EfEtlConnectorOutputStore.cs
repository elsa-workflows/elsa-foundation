using System.Data;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    public async ValueTask<EtlConnectorOutputResult> RetainConnectorBatchOutputAsync(EtlConnectorPartitionLease lease,
        string batchId, EtlConnectorBatchOutput output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(output);
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
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
        var state = await ReadConnectorStateAsync(invocation, connector, cancellationToken);
        var position = await ReadConnectorPositionAsync(partition, state.Pipeline, state.Bindings, state.Prepared, cancellationToken);
        if (position != lease.Position || partition.OwedBatchId != batchId) return new(EtlStoreStatus.Conflict, null);
        var admission = new EtlConnectorRunAdmission(invocation, state.Bindings);
        var owed = await ReadConnectorOwedBatchAsync(partition, admission, state.Pipeline, state.Prepared!, position, cancellationToken);
        if (owed is null) return new(EtlStoreStatus.Conflict, null);
        var batch = await db.ConnectorBatches.SingleAsync(row => row.ScopeKey == key.ScopeKey && row.RunKey == key.RunKey &&
            row.PartitionOrdinal == lease.PartitionOrdinal && row.BatchId == batchId, cancellationToken);
        var sourceRequest = position is EtlConnectorHttpPosition httpPosition
            ? await HttpRequestForUpdateAsync(partition, httpPosition.PageSequence, cancellationToken)
            : null;
        // Replaying output retention is the pre-dispatch capacity check for migrated checkpoints.
        var reserveDelta = MissingAcceptanceReserve(partition, position, owed.Batch, batch);
        if (owed.Output is { } retained)
        {
            if (retained != output) return new(EtlStoreStatus.Conflict, null);
            if (reserveDelta > state.Pipeline.Http.MaxRetainedMetadataBytes - connector.RetainedMetadataBytes)
            {
                var unavoidable = checked(MetadataBytes(partition.PositionJson) +
                    (sourceRequest is null ? 0 : sourceRequest.FetchMetadataBytes + sourceRequest.SourceMetadataReserveBytes) +
                    batch.AcceptanceMetadataBytes + reserveDelta);
                return new(await ConnectorMetadataBudgetStatusAsync(connector, state.Pipeline.Http, partition.PartitionOrdinal, unavoidable, cancellationToken), null);
            }
            if (reserveDelta != 0)
            {
                RetainAcceptanceReserve(connector, partition, batch, reserveDelta);
                partition.Revision = checked(partition.Revision + 1);
                connector.Revision = checked(connector.Revision + 1);
                run.Revision = checked(run.Revision + 1);
                run.UpdatedAtUtcTicks = NowTicks();
                await db.SaveChangesAsync(cancellationToken);
            }
            if (!HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks())) return new(EtlStoreStatus.LeaseLost, null);
            await tx.CommitAsync(cancellationToken);
            return new(EtlStoreStatus.Replay, retained);
        }
        string json;
        long bytes;
        try
        {
            bytes = ValidateConnectorOutput(output, batch.RecordCount, admission, batchId);
            json = EtlConnectorRecordCodec.Write(output);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or OverflowException)
        {
            return new(EtlStoreStatus.Invalid, null);
        }
        var metadata = MetadataBytes(json);
        if (bytes > state.Pipeline.Limits.MaxOutputBatchBytes ||
            bytes > state.Pipeline.Limits.MaxRetainedOutputBytes - connector.RetainedOutputBytes)
            return new(EtlStoreStatus.PermanentBudgetExceeded, null);
        if (checked(metadata + reserveDelta) > state.Pipeline.Http.MaxRetainedMetadataBytes - connector.RetainedMetadataBytes)
        {
            var unavoidable = checked(MetadataBytes(partition.PositionJson) +
                (sourceRequest is null ? 0 : sourceRequest.FetchMetadataBytes + sourceRequest.SourceMetadataReserveBytes) +
                batch.AcceptanceMetadataBytes + reserveDelta + metadata);
            return new(await ConnectorMetadataBudgetStatusAsync(connector, state.Pipeline.Http, partition.PartitionOrdinal, unavoidable, cancellationToken), null);
        }
        RetainAcceptanceReserve(connector, partition, batch, reserveDelta);
        batch.OutputJson = json;
        batch.OutputDigest = MetadataDigest(json);
        batch.OutputBytes = bytes;
        batch.MainEffectKind = output.Main is { } main ? (int)main.Kind : null;
        batch.ErrorEffectKind = output.Errors is { } errors ? (int)errors.Kind : null;
        batch.MetadataBytes = checked(batch.MetadataBytes + metadata);
        partition.BatchMetadataBytes = checked(partition.BatchMetadataBytes + metadata);
        partition.MetadataBytes = checked(partition.MetadataBytes + metadata);
        partition.RetainedOutputBytes = checked(partition.RetainedOutputBytes + bytes);
        partition.Revision = checked(partition.Revision + 1);
        connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes + metadata);
        connector.RetainedOutputBytes = checked(connector.RetainedOutputBytes + bytes);
        connector.Revision = checked(connector.Revision + 1);
        run.UpdatedAtUtcTicks = NowTicks();
        run.Revision = checked(run.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (!HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks())) return new(EtlStoreStatus.LeaseLost, null);
        await tx.CommitAsync(cancellationToken);
        return new(EtlStoreStatus.Applied, output);
    }

    private static EtlConnectorBatchOutput? ReadConnectorBatchOutput(EtlConnectorBatchRecord batch)
    {
        if (batch.OutputJson is null)
        {
            if (batch.OutputDigest is not null || batch.OutputBytes != 0 || batch.MainEffectKind is not null || batch.ErrorEffectKind is not null)
                throw new InvalidDataException("The connector batch has output accounting without a plan.");
            return null;
        }
        try
        {
            var output = EtlConnectorRecordCodec.Read<EtlConnectorBatchOutput>(batch.OutputJson);
            if (batch.OutputDigest != MetadataDigest(batch.OutputJson) ||
                batch.OutputBytes != ValidateConnectorOutputShape(output, batch.RecordCount) ||
                batch.MainEffectKind != (output.Main is { } main ? (int)main.Kind : null) ||
                batch.ErrorEffectKind != (output.Errors is { } errors ? (int)errors.Kind : null))
                throw new InvalidDataException("The retained connector output is inconsistent.");
            return output;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new InvalidDataException("The retained connector output is invalid.", exception);
        }
    }

    private static long ValidateConnectorOutput(EtlConnectorBatchOutput output, long records,
        EtlConnectorRunAdmission admission, string batchId)
    {
        var bytes = ValidateConnectorOutputShape(output, records);
        ValidateRoute(output.Main, EtlOutputRoute.Main, admission.Bindings.Main);
        ValidateRoute(output.Errors, EtlOutputRoute.Error, admission.Bindings.Errors);
        return bytes;

        void ValidateRoute(EtlConnectorRouteOutput? route, EtlOutputRoute slot, EtlConnectorBindingRevision binding)
        {
            if (route is null) return;
            if (route.Kind != binding.Kind || route.EffectId != EtlRuntimeIdentity.ConnectorEffectId(admission, batchId,
                    slot, route.Body.ContentHash, route.Body.ByteLength, route.RecordCount))
                throw new InvalidDataException("The connector route does not match its pinned output identity.");
        }
    }

    private static long ValidateConnectorOutputShape(EtlConnectorBatchOutput output, long records)
    {
        if (output.AcceptedRecords < 0 || output.RejectedRecords < 0 || output.FilteredRecords < 0 ||
            checked(output.AcceptedRecords + output.RejectedRecords + output.FilteredRecords) != records)
            throw new InvalidDataException("The connector output does not account for its input records.");
        return checked(RouteBytes(output.Main, output.AcceptedRecords) + RouteBytes(output.Errors, output.RejectedRecords));

        static long RouteBytes(EtlConnectorRouteOutput? route, long count)
        {
            if (route is null)
            {
                if (count != 0) throw new InvalidDataException("The connector output is missing a route body.");
                return 0;
            }
            EtlRuntimeRecordCodec.ValidateObject(route.Body, "route body");
            if (count <= 0 || route.RecordCount != count || route.Body.ByteLength <= 0 ||
                route.Kind is not (EtlConnectorKind.Http or EtlConnectorKind.Jsonl) || !EtlRuntimeRecordCodec.IsSha256(route.EffectId))
                throw new InvalidDataException("The connector route body is invalid.");
            return route.Body.ByteLength;
        }
    }
}
