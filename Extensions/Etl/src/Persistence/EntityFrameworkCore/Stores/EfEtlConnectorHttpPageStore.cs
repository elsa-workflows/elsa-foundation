using System.Data;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    public ValueTask<EtlConnectorPositionResult> AttachHttpPageAsync(EtlConnectorPartitionLease lease,
        EtlRetainedHttpPage page, CancellationToken cancellationToken = default) =>
        TransitionHttpPageAsync(lease, page, advance: false, cancellationToken);

    public ValueTask<EtlConnectorPositionResult> AdvanceEmptyHttpPageAsync(EtlConnectorPartitionLease lease,
        CancellationToken cancellationToken = default) => TransitionHttpPageAsync(lease, null, advance: true, cancellationToken);

    private async ValueTask<EtlConnectorPositionResult> TransitionHttpPageAsync(EtlConnectorPartitionLease lease,
        EtlRetainedHttpPage? page, bool advance, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(lease);
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
        var expiry = row.ExpiresAtUtcTicks!.Value;
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
        var state = await ReadConnectorStateAsync(invocation, connector, cancellationToken);
        var position = await ReadConnectorPositionAsync(row, state.Pipeline, state.Bindings, state.Prepared, cancellationToken);
        if (position is not EtlConnectorHttpPosition current || lease.Position is not EtlConnectorHttpPosition expected)
            return new(EtlStoreStatus.Invalid, null);
        // Replays return current durable responsibility, including a batch reserved after attachment.
        lease = lease with { OwedBatch = await ReadConnectorOwedBatchAsync(row, new(invocation, state.Bindings),
            state.Pipeline, state.Prepared!, position, cancellationToken) };
        EtlConnectorHttpPosition next;
        EtlConnectorHttpPageRecord? addedPage = null;
        EtlConnectorHttpRequestRecord? addedRequest = null;
        if (advance)
        {
            if (expected.Page is not { RecordCount: 0 } || expected.NextItem != 0 || expected.Exhausted)
                return new(EtlStoreStatus.Conflict, null);
            try
            {
                EtlConnectorRecordCodec.Complete(expected);
                EtlConnectorRecordCodec.ValidateHttpPosition(expected, state.Pipeline.Http);
                next = AfterHttpPage(expected);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or OverflowException)
            {
                return new(EtlStoreStatus.Invalid, null);
            }
            // Identical progress replay is observable only under still-current ownership. It must
            // retain the original page, not merely resemble the requested next position.
            if (current == next)
            {
                var retained = await HttpPageRowAsync(row, expected.PageSequence, cancellationToken);
                if (retained is not null && ReadHttpPage(retained) == expected.Page)
                    return PositionResult(EtlStoreStatus.Replay, lease, row, current);
            }
            if (current != expected) return new(EtlStoreStatus.Conflict, null);
            if (!next.Exhausted) addedRequest = HttpRequestRecord(row, next);
        }
        else
        {
            if (page is null || expected.Exhausted) return new(EtlStoreStatus.Invalid, null);
            // A lost attachment reply may be retried after batches on this same page progressed.
            // Return current progress and responsibility; never replace or rewind the retained page.
            if (current.Page == page && expected.PageSequence == current.PageSequence && expected.Request == current.Request &&
                expected.NextItem >= 0 && expected.NextItem <= current.NextItem && (expected.Page is null || expected.Page == page))
                return PositionResult(EtlStoreStatus.Replay, lease, row, current);
            try
            {
                // The request is taken from the expected lease, never supplied by the response.
                next = expected with { Page = page };
                EtlConnectorRecordCodec.Complete(next);
                EtlConnectorRecordCodec.ValidateHttpPosition(next, state.Pipeline.Http);
                if (expected.NextItem != 0 || page.NextContinuation == string.Empty ||
                    state.Pipeline.Source is EtlHttpJsonSource { Pagination: EtlSinglePagePagination } && page.NextContinuation is not null ||
                    page.NextContinuation is not null && page.PageSequence + 1 >= state.Pipeline.Http.MaxPagesPerPartition)
                    return new(EtlStoreStatus.Invalid, null);
                EtlConnectorRecordCodec.Write<EtlConnectorPosition>(next);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or OverflowException)
            {
                return new(EtlStoreStatus.Invalid, null);
            }
            if (current.Page is not null) return new(EtlStoreStatus.Conflict, null);
            if (current != expected || expected.Page is not null) return new(EtlStoreStatus.Conflict, null);
            if (page.NextContinuation is { } continuation)
            {
                var nextHash = EtlRuntimeIdentity.HttpRequestHash(current.Request with { Continuation = continuation });
                if (nextHash == page.RequestHash || await db.ConnectorHttpRequests.AnyAsync(x => x.ScopeKey == row.ScopeKey &&
                    x.RunKey == row.RunKey && x.PartitionOrdinal == row.PartitionOrdinal && x.RequestHash == nextHash, cancellationToken))
                    return new(EtlStoreStatus.Invalid, null);
            }
            // The current-page lookup and request-ledger keys already prove this is a new
            // attachment. A second history-wide sequence-or-hash probe is unnecessary.
            // Subtraction avoids overflow even for a caller supplying an extremely large object.
            if (page.Body.ByteLength > state.Pipeline.Http.MaxRetainedInputBytes - connector.RetainedInputBytes)
                return new(EtlStoreStatus.BudgetExceeded, null);
            var pageJson = EtlConnectorRecordCodec.Write(page);
            addedPage = new EtlConnectorHttpPageRecord
            {
                ScopeKey = row.ScopeKey, RunKey = row.RunKey, PartitionOrdinal = row.PartitionOrdinal,
                PageSequence = page.PageSequence, RequestHash = page.RequestHash, PageJson = pageJson,
                PageDigest = MetadataDigest(pageJson), MetadataBytes = MetadataBytes(pageJson),
                NextRequestHash = EtlRuntimeIdentity.HttpRequestHash(current.Request with { Continuation = page.NextContinuation }),
                HasContinuation = page.NextContinuation is not null,
                ByteLength = page.Body.ByteLength, SchemaVersion = EtlRuntimeEfModule.SchemaVersion
            };
        }
        var positionJson = EtlConnectorRecordCodec.Write<EtlConnectorPosition>(next);
        var metadataDelta = checked(MetadataBytes(positionJson) - MetadataBytes(row.PositionJson) +
            (addedPage?.MetadataBytes ?? 0) + (addedRequest?.MetadataBytes ?? 0));
        if (metadataDelta > state.Pipeline.Http.MaxRetainedMetadataBytes - connector.RetainedMetadataBytes)
            return new(EtlStoreStatus.BudgetExceeded, null);
        connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes + metadataDelta);
        row.MetadataBytes = checked(row.MetadataBytes + metadataDelta);
        connector.Revision = checked(connector.Revision + 1);
        if (addedPage is not null)
        {
            db.ConnectorHttpPages.Add(addedPage);
            connector.RetainedInputBytes = checked(connector.RetainedInputBytes + addedPage.ByteLength);
            row.PageBytes = checked(row.PageBytes + addedPage.ByteLength);
            row.PageCount = checked(row.PageCount + 1);
        }
        if (addedRequest is not null)
        {
            db.ConnectorHttpRequests.Add(addedRequest);
            row.CurrentRequestSequence = addedRequest.PageSequence;
            row.CurrentRequestHash = addedRequest.RequestHash;
        }
        row.PositionJson = positionJson;
        row.Completed = next.Exhausted;
        row.Revision = checked(row.Revision + 1);
        run.UpdatedAtUtcTicks = NowTicks();
        run.Revision = checked(run.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (advance) await CompleteConnectorRunIfReadyAsync(key, run, invocation, cancellationToken);
        if (expiry <= NowTicks()) return new(EtlStoreStatus.LeaseLost, null);
        await tx.CommitAsync(cancellationToken);
        return PositionResult(EtlStoreStatus.Applied, lease, row, next);
    }

    private static EtlConnectorHttpPosition AfterHttpPage(EtlConnectorHttpPosition position) =>
        position.Page!.NextContinuation is { } continuation
            ? new(checked(position.PageSequence + 1), 0, position.Request with { Continuation = continuation }, null, false)
            : position with { NextItem = 0, Page = null, Exhausted = true };

    private EtlConnectorPositionResult PositionResult(EtlStoreStatus status, EtlConnectorPartitionLease lease,
        EtlConnectorPartitionRecord row, EtlConnectorPosition position) =>
        row.ExpiresAtUtcTicks is { } expiry && expiry > NowTicks()
            ? new(status, lease with { Position = position, ExpiresAt = EtlRuntimeRecordCodec.ReadUtc(expiry) })
            : new(EtlStoreStatus.LeaseLost, null);

}
