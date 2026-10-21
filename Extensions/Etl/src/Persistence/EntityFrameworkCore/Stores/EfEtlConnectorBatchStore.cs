using System.Data;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    public async ValueTask<EtlConnectorReservationResult> ReserveConnectorBatchAsync(EtlConnectorPartitionLease lease,
        EtlConnectorBatchDescriptor batch, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(batch);
        var key = RunKey(lease.Scope, lease.RunId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        if (run is null) return new(EtlStoreStatus.NotFound, null);
        if (run.Status >= (int)EtlRunStatus.Succeeded) return new(EtlStoreStatus.Terminal, null);
        var connector = await ConnectorRowAsync(key, cancellationToken);
        if (connector is null) return new(EtlStoreStatus.Invalid, null);
        var row = await db.ConnectorPartitions.SingleOrDefaultAsync(x => x.ScopeKey == key.ScopeKey &&
            x.RunKey == key.RunKey && x.PartitionOrdinal == lease.PartitionOrdinal, cancellationToken);
        if (row is null) return new(EtlStoreStatus.NotFound, null);
        if (run.Status != (int)EtlRunStatus.Running || !HasConnectorAuthority(row, lease.Owner, lease.Fence, NowTicks()))
            return new(EtlStoreStatus.LeaseLost, null);
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
        var state = await ReadConnectorStateAsync(invocation, connector, cancellationToken);
        var position = await ReadConnectorPositionAsync(row, state.Pipeline, state.Bindings, state.Prepared, cancellationToken);
        if (lease.Position != position) return new(EtlStoreStatus.Conflict, null);
        var admission = new EtlConnectorRunAdmission(invocation, state.Bindings);
        var owed = await ReadConnectorOwedBatchAsync(row, admission, state.Pipeline, state.Prepared!, position, cancellationToken);
        if (owed is not null)
            return HasConnectorAuthority(row, lease.Owner, lease.Fence, NowTicks())
                ? new(owed.Batch == batch ? EtlStoreStatus.Replay : EtlStoreStatus.Conflict, owed.Batch == batch ? owed : null)
                : new(EtlStoreStatus.LeaseLost, null);

        long records;
        string descriptor;
        try
        {
            records = ValidateConnectorBatch(batch, admission, state.Prepared!, row.PartitionOrdinal, position);
            descriptor = EtlConnectorRecordCodec.Write(batch);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or OverflowException)
        {
            return new(EtlStoreStatus.Invalid, null);
        }
        var limits = state.Pipeline.Limits;
        var metadataBytes = MetadataBytes(descriptor);
        if (records > limits.MaxBatchRecords || batch.InputBytes > limits.MaxBatchBytes ||
            run.OutstandingRecords > limits.MaxOutstandingRecords - records ||
            run.OutstandingBytes > limits.MaxOutstandingBytes - batch.InputBytes ||
            metadataBytes > state.Pipeline.Http.MaxRetainedMetadataBytes - connector.RetainedMetadataBytes)
            return new(EtlStoreStatus.BudgetExceeded, null);
        var startKey = ConnectorBatchStartKey(batch.Range);
        if (await db.ConnectorBatches.AnyAsync(x => x.ScopeKey == key.ScopeKey && x.RunKey == key.RunKey &&
            x.PartitionOrdinal == row.PartitionOrdinal && (x.BatchId == batch.BatchId || x.StartKey == startKey), cancellationToken))
            return new(EtlStoreStatus.Conflict, null);
        db.ConnectorBatches.Add(new EtlConnectorBatchRecord
        {
            ScopeKey = key.ScopeKey, RunKey = key.RunKey, PartitionOrdinal = row.PartitionOrdinal,
            BatchId = batch.BatchId, StartKey = startKey, DescriptorJson = descriptor, DescriptorDigest = MetadataDigest(descriptor),
            RecordCount = records, InputBytes = batch.InputBytes, MetadataBytes = metadataBytes,
            SchemaVersion = EtlRuntimeEfModule.SchemaVersion
        });
        row.OwedBatchId = batch.BatchId;
        row.BatchMetadataBytes = checked(row.BatchMetadataBytes + metadataBytes);
        row.MetadataBytes = checked(row.MetadataBytes + metadataBytes);
        row.Revision = checked(row.Revision + 1);
        connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes + metadataBytes);
        connector.Revision = checked(connector.Revision + 1);
        run.OutstandingRecords = checked(run.OutstandingRecords + records);
        run.OutstandingBytes = checked(run.OutstandingBytes + batch.InputBytes);
        run.UpdatedAtUtcTicks = NowTicks();
        run.Revision = checked(run.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (!HasConnectorAuthority(row, lease.Owner, lease.Fence, NowTicks())) return new(EtlStoreStatus.LeaseLost, null);
        await tx.CommitAsync(cancellationToken);
        return new(EtlStoreStatus.Applied, new(batch, false));
    }

    private async ValueTask<EtlConnectorBatchReservation?> ReadConnectorOwedBatchAsync(EtlConnectorPartitionRecord row,
        EtlConnectorRunAdmission admission, EtlPipelineV2 pipeline, EtlConnectorPreparedRun prepared, EtlConnectorPosition position, CancellationToken cancellationToken)
    {
        if (row.OwedBatchId is null) return null;
        var retained = await db.ConnectorBatches.AsNoTracking().SingleOrDefaultAsync(batch => batch.ScopeKey == row.ScopeKey &&
            batch.RunKey == row.RunKey && batch.PartitionOrdinal == row.PartitionOrdinal && batch.BatchId == row.OwedBatchId, cancellationToken)
            ?? throw new InvalidDataException("The connector owed batch is missing.");
        var descriptor = ReadConnectorBatch(retained);
        var records = ValidateConnectorBatch(descriptor, admission, prepared, row.PartitionOrdinal, position);
        if (retained.Settled || records != retained.RecordCount || row.Completed ||
            records > pipeline.Limits.MaxBatchRecords || descriptor.InputBytes > pipeline.Limits.MaxBatchBytes)
            throw new InvalidDataException("The connector owed batch does not match its partition.");
        var output = ReadConnectorBatchOutput(retained);
        if (output is not null && ValidateConnectorOutput(output, records, admission, descriptor.BatchId) > pipeline.Limits.MaxOutputBatchBytes)
            throw new InvalidDataException("The connector output exceeds its admitted batch bound.");
        var deliveries = ReadConnectorHttpDeliveries(retained, output, pipeline.Http);
        if (row.NotBeforeUtcTicks != ConnectorNotBefore(output, deliveries))
            throw new InvalidDataException("The partition retry eligibility differs from its owed output.");
        return new(descriptor, false) { Output = output, HttpDeliveries = deliveries };
    }

    private static EtlConnectorBatchDescriptor ReadConnectorBatch(EtlConnectorBatchRecord row)
    {
        EnsureSchema(row.SchemaVersion);
        var batch = EtlConnectorRecordCodec.Read<EtlConnectorBatchDescriptor>(row.DescriptorJson);
        if (row.DescriptorDigest != MetadataDigest(row.DescriptorJson) || row.MetadataBytes != checked(MetadataBytes(row.DescriptorJson) + (row.OutputJson is null ? 0 : MetadataBytes(row.OutputJson)) +
                HttpDeliveryMetadataBytes(row.HttpDeliveryJson)) ||
            batch.BatchId != row.BatchId || row.StartKey != ConnectorBatchStartKey(batch.Range) || batch.InputBytes != row.InputBytes ||
            batch.InputBytes <= 0 || !EtlRuntimeRecordCodec.IsSha256(batch.InputHash) || row.RecordCount <= 0)
            throw new InvalidDataException("The connector batch metadata is inconsistent.");
        ReadConnectorBatchOutput(row);
        ValidateHttpDeliveryEnvelope(row);
        return batch;
    }

    private static long ValidateConnectorBatch(EtlConnectorBatchDescriptor batch, EtlConnectorRunAdmission admission,
        EtlConnectorPreparedRun prepared, int partitionOrdinal, EtlConnectorPosition position)
    {
        if (batch.InputBytes <= 0 || !EtlRuntimeRecordCodec.IsSha256(batch.InputHash) ||
            batch.BatchId != EtlRuntimeIdentity.ConnectorBatchId(admission, prepared, partitionOrdinal, batch.Range, batch.InputBytes, batch.InputHash))
            throw new InvalidDataException("The connector batch does not match its admitted input.");
        return (batch.Range, position) switch
        {
            (EtlConnectorFileBatchRange range, EtlConnectorFilePosition file) when range.StartOrdinal == file.NextOrdinal &&
                range.EndOrdinalExclusive > range.StartOrdinal && range.EndOrdinalExclusive <= file.EndOrdinalExclusive =>
                range.EndOrdinalExclusive - range.StartOrdinal,
            (EtlConnectorHttpBatchRange range, EtlConnectorHttpPosition { Exhausted: false, Page: { } page } http) when
                range.PageSequence == http.PageSequence && range.RequestHash == page.RequestHash && range.PageContentHash == page.Body.ContentHash &&
                range.StartItem == http.NextItem && range.EndItemExclusive > range.StartItem && range.EndItemExclusive <= page.RecordCount =>
                range.EndItemExclusive - range.StartItem,
            _ => throw new InvalidDataException("The connector batch does not match the current source position.")
        };
    }

    private static string ConnectorBatchStartKey(EtlConnectorBatchRange range) => MetadataDigest(range switch
    {
        EtlConnectorFileBatchRange file => EtlRuntimeJson.Write<EtlConnectorBatchRange>(file with { EndOrdinalExclusive = file.StartOrdinal }),
        EtlConnectorHttpBatchRange http => EtlRuntimeJson.Write<EtlConnectorBatchRange>(http with { EndItemExclusive = http.StartItem }),
        _ => throw new InvalidDataException("The connector batch range is unsupported.")
    });

    private async ValueTask ValidateConnectorOutstandingAsync(EtlConnectorRunRecord connector, EtlPipelineV2 pipeline, CancellationToken cancellationToken)
    {
        // Follow at most one indexed owed pointer per bounded partition, not all historical batches.
        var owed = await (from partition in db.ConnectorPartitions
            join batch in db.ConnectorBatches on new { partition.ScopeKey, partition.RunKey, partition.PartitionOrdinal, BatchId = partition.OwedBatchId }
                equals new { batch.ScopeKey, batch.RunKey, batch.PartitionOrdinal, batch.BatchId }
            where partition.ScopeKey == connector.ScopeKey && partition.RunKey == connector.RunKey
            select new { batch.RecordCount, batch.InputBytes, batch.Settled }).ToListAsync(cancellationToken);
        var run = await db.Runs.SingleAsync(row => row.ScopeKey == connector.ScopeKey && row.RunKey == connector.RunKey, cancellationToken);
        if (run.OutstandingRecords > pipeline.Limits.MaxOutstandingRecords || run.OutstandingBytes > pipeline.Limits.MaxOutstandingBytes ||
            owed.Any(batch => batch.Settled || batch.RecordCount <= 0 || batch.InputBytes <= 0) ||
            owed.Sum(batch => batch.RecordCount) != run.OutstandingRecords || owed.Sum(batch => batch.InputBytes) != run.OutstandingBytes)
            throw new InvalidDataException("The connector outstanding accounting differs from its owed batches.");
    }

    private async ValueTask AuditConnectorBatchHistoryAsync(EtlConnectorPartitionRecord partition, CancellationToken cancellationToken)
    {
        long metadataBytes = 0, outputBytes = 0;
        var owedFound = false;
        await foreach (var retained in db.ConnectorBatches.AsNoTracking().Where(batch => batch.ScopeKey == partition.ScopeKey &&
            batch.RunKey == partition.RunKey && batch.PartitionOrdinal == partition.PartitionOrdinal).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            ReadConnectorBatch(retained);
            // Acceptance is not enabled yet. A settled row cannot authorize progress without
            // the route acknowledgement and accepted-range history introduced by that transition.
            if (retained.Settled || retained.BatchId != partition.OwedBatchId || owedFound)
                throw new InvalidDataException("The connector reservation history has unsupported or unlinked responsibility.");
            owedFound = true;
            metadataBytes = checked(metadataBytes + retained.MetadataBytes);
            outputBytes = checked(outputBytes + retained.OutputBytes);
        }
        if (metadataBytes != partition.BatchMetadataBytes || outputBytes != partition.RetainedOutputBytes || owedFound != (partition.OwedBatchId is not null))
            throw new InvalidDataException("The connector reservation history or metadata accounting is incomplete.");
    }
}
