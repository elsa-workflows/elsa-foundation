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
        var state = await ReadConnectorStateAsync(EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson), connector, cancellationToken);
        var position = await ReadConnectorPositionAsync(row, state.Pipeline, state.Bindings, state.Prepared, cancellationToken);
        if (position is not EtlConnectorHttpPosition current || lease.Position is not EtlConnectorHttpPosition expected)
            return new(EtlStoreStatus.Invalid, null);
        EtlConnectorHttpPosition next;
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
        }
        else
        {
            if (page is null || expected.Exhausted) return new(EtlStoreStatus.Invalid, null);
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
            if (current.Page is not null)
            {
                // Permit retry of attachment with the pre-attachment lease, or the returned lease.
                if (current == next && (expected == current || expected == (current with { Page = null })))
                    return PositionResult(EtlStoreStatus.Replay, lease, row, current);
                return new(EtlStoreStatus.Conflict, null);
            }
            if (current != expected || expected.Page is not null) return new(EtlStoreStatus.Conflict, null);
            if (page.NextContinuation is { } continuation)
            {
                var nextHash = EtlRuntimeIdentity.HttpRequestHash(current.Request with { Continuation = continuation });
                if (nextHash == page.RequestHash || await db.ConnectorHttpPages.AnyAsync(x => x.ScopeKey == row.ScopeKey &&
                    x.RunKey == row.RunKey && x.PartitionOrdinal == row.PartitionOrdinal && x.RequestHash == nextHash, cancellationToken))
                    return new(EtlStoreStatus.Invalid, null);
            }
            if (await db.ConnectorHttpPages.AnyAsync(x => x.ScopeKey == row.ScopeKey && x.RunKey == row.RunKey &&
                x.PartitionOrdinal == row.PartitionOrdinal && (x.PageSequence == page.PageSequence || x.RequestHash == page.RequestHash), cancellationToken))
                return new(EtlStoreStatus.Conflict, null);
            // Subtraction avoids overflow even for a caller supplying an extremely large object.
            if (page.Body.ByteLength > state.Pipeline.Http.MaxRetainedInputBytes - connector.RetainedInputBytes)
                return new(EtlStoreStatus.BudgetExceeded, null);
            db.ConnectorHttpPages.Add(new EtlConnectorHttpPageRecord
            {
                ScopeKey = row.ScopeKey, RunKey = row.RunKey, PartitionOrdinal = row.PartitionOrdinal,
                PageSequence = page.PageSequence, RequestHash = page.RequestHash, PageJson = EtlConnectorRecordCodec.Write(page),
                ByteLength = page.Body.ByteLength, SchemaVersion = EtlRuntimeEfModule.SchemaVersion
            });
            connector.RetainedInputBytes = checked(connector.RetainedInputBytes + page.Body.ByteLength);
            connector.Revision = checked(connector.Revision + 1);
        }
        row.PositionJson = EtlConnectorRecordCodec.Write<EtlConnectorPosition>(next);
        row.Completed = next.Exhausted;
        row.Revision = checked(row.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (expiry <= NowTicks()) return new(EtlStoreStatus.LeaseLost, null);
        await tx.CommitAsync(cancellationToken);
        return PositionResult(EtlStoreStatus.Applied, lease, row, next);
    }

    private static EtlConnectorHttpPosition AfterHttpPage(EtlConnectorHttpPosition position) =>
        position.Page!.NextContinuation is { } continuation
            ? new(checked(position.PageSequence + 1), 0, position.Request with { Continuation = continuation }, null, false)
            : position with { NextItem = 0, Page = null, Exhausted = true };

    private EtlConnectorPositionResult PositionResult(EtlStoreStatus status, EtlConnectorPartitionLease lease,
        EtlConnectorPartitionRecord row, EtlConnectorHttpPosition position) =>
        row.ExpiresAtUtcTicks is { } expiry && expiry > NowTicks()
            ? new(status, lease with { Position = position, ExpiresAt = EtlRuntimeRecordCodec.ReadUtc(expiry) })
            : new(EtlStoreStatus.LeaseLost, null);

    private async ValueTask<(EtlPipelineV2 Pipeline, EtlConnectorRunBindings Bindings, EtlConnectorPreparedRun? Prepared)> ReadConnectorStateAsync(
        EtlInvocation invocation, EtlConnectorRunRecord connector, CancellationToken cancellationToken)
    {
        var pageBytes = await db.ConnectorHttpPages.Where(page => page.ScopeKey == connector.ScopeKey && page.RunKey == connector.RunKey)
            .SumAsync(page => (long?)page.ByteLength, cancellationToken) ?? 0;
        return ReadConnectorState(invocation, connector, pageBytes);
    }

    private async ValueTask<EtlConnectorPosition> ReadConnectorPositionAsync(EtlConnectorPartitionRecord row, EtlPipelineV2 pipeline,
        EtlConnectorRunBindings bindings, EtlConnectorPreparedRun? prepared, CancellationToken cancellationToken)
    {
        var position = ReadConnectorPosition(row, pipeline, bindings, prepared);
        var pages = db.ConnectorHttpPages.AsNoTracking().Where(page => page.ScopeKey == row.ScopeKey && page.RunKey == row.RunKey && page.PartitionOrdinal == row.PartitionOrdinal);
        var count = await pages.LongCountAsync(cancellationToken);
        if (position is not EtlConnectorHttpPosition http)
        {
            if (count != 0) throw new InvalidDataException("A file partition has HTTP page history.");
            return position;
        }
        var expectedCount = http.PageSequence + (http.Page is not null || http.Exhausted ? 1 : 0);
        if (count != expectedCount || await pages.AnyAsync(page => page.PageSequence < 0 || page.PageSequence >= expectedCount, cancellationToken))
            throw new InvalidDataException("The HTTP position has incomplete or inconsistent page history.");
        // Stream the bounded history so a changed older digest cannot silently remove a visited
        // request. Do not materialize every retained metadata document at once.
        var request = new EtlHttpRequest(http.Request.ShardId, null);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        long sequence = 0;
        await foreach (var retained in pages.OrderBy(page => page.PageSequence).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var page = ReadHttpPage(retained);
            if (page.PageSequence != sequence || !visited.Add(page.RequestHash))
                throw new InvalidDataException("The HTTP page history does not follow its request chain.");
            var retainedPosition = new EtlConnectorHttpPosition(sequence, 0, request, page, false);
            EtlConnectorRecordCodec.Complete(retainedPosition);
            EtlConnectorRecordCodec.ValidateHttpPosition(retainedPosition, pipeline.Http);
            if (page.NextContinuation == string.Empty ||
                pipeline.Source is EtlHttpJsonSource { Pagination: EtlSinglePagePagination } && page.NextContinuation is not null ||
                page.NextContinuation is not null && (sequence + 1 >= pipeline.Http.MaxPagesPerPartition ||
                    visited.Contains(EtlRuntimeIdentity.HttpRequestHash(request with { Continuation = page.NextContinuation }))))
                throw new InvalidDataException("The retained continuation violates the finite request history.");
            if (sequence == http.PageSequence)
            {
                if (request != http.Request || http.Page is not null && page != http.Page ||
                    http.Exhausted && (page.NextContinuation is not null || page.RecordCount != 0))
                    throw new InvalidDataException("The HTTP position differs from its retained page.");
            }
            else
            {
                // Nonempty progress will require accepted-batch evidence when that capability is added.
                if (page.NextContinuation is null || page.RecordCount != 0)
                    throw new InvalidDataException("The HTTP position has no accepted predecessor progress.");
                request = request with { Continuation = page.NextContinuation };
            }
            sequence++;
        }
        if (http.Page is null && !http.Exhausted && request != http.Request)
            throw new InvalidDataException("The HTTP request does not follow its retained predecessor.");
        return position;
    }

    private Task<EtlConnectorHttpPageRecord?> HttpPageRowAsync(EtlConnectorPartitionRecord row, long sequence, CancellationToken cancellationToken) =>
        db.ConnectorHttpPages.SingleOrDefaultAsync(page => page.ScopeKey == row.ScopeKey && page.RunKey == row.RunKey &&
            page.PartitionOrdinal == row.PartitionOrdinal && page.PageSequence == sequence, cancellationToken);

    private static EtlRetainedHttpPage ReadHttpPage(EtlConnectorHttpPageRecord row)
    {
        EnsureSchema(row.SchemaVersion);
        var page = EtlConnectorRecordCodec.Read<EtlRetainedHttpPage>(row.PageJson);
        if (page.PageSequence != row.PageSequence || page.RequestHash != row.RequestHash || page.Body is null ||
            page.Body.ByteLength != row.ByteLength || row.ByteLength < 0)
            throw new InvalidDataException("The HTTP page history metadata differs from its retained page.");
        return page;
    }
}
