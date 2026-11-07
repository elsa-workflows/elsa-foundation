using System.Data;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    public ValueTask<EtlConnectorHttpSourceFetchResult> BeginConnectorHttpSourceAsync(EtlConnectorPartitionLease lease,
        string requestId, CancellationToken cancellationToken = default)
    {
        PrepareOperation(nameof(BeginConnectorHttpSourceAsync), lease?.Scope, lease?.RunId, cancellationToken);
        return RunOperationAsync(nameof(BeginConnectorHttpSourceAsync), lease?.Scope, lease?.RunId,
            () => BeginConnectorHttpSourceOnceAsync(lease, requestId, cancellationToken), cancellationToken);
    }

    private ValueTask<EtlConnectorHttpSourceFetchResult> BeginConnectorHttpSourceOnceAsync(EtlConnectorPartitionLease? lease,
        string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return MutateConnectorHttpSourceAsync(lease, (fetch, position, policy, now) =>
            EtlHttpSourceFetchTransitions.Begin(fetch, EtlRuntimeIdentity.HttpRequestHash(position.Request),
                position.PageSequence, requestId, lease.Fence, now, policy), cancellationToken);
    }

    public ValueTask<EtlConnectorHttpSourceFetchResult> CompleteConnectorHttpSourceAsync(EtlConnectorPartitionLease lease,
        EtlHttpSourceFetchAttempt attempt, EtlHttpSourceFetchOutcome outcome, CancellationToken cancellationToken = default)
    {
        PrepareOperation(nameof(CompleteConnectorHttpSourceAsync), lease?.Scope, lease?.RunId, cancellationToken);
        return RunOperationAsync(nameof(CompleteConnectorHttpSourceAsync), lease?.Scope, lease?.RunId,
            () => CompleteConnectorHttpSourceOnceAsync(lease, attempt, outcome, cancellationToken), cancellationToken);
    }

    private ValueTask<EtlConnectorHttpSourceFetchResult> CompleteConnectorHttpSourceOnceAsync(EtlConnectorPartitionLease? lease,
        EtlHttpSourceFetchAttempt attempt, EtlHttpSourceFetchOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return MutateConnectorHttpSourceAsync(lease, (fetch, _, policy, now) => fetch is null
            ? new(EtlStoreStatus.Conflict, null, null)
            : EtlHttpSourceFetchTransitions.Complete(fetch, attempt, outcome, lease.Fence, now, policy), cancellationToken);
    }

    public ValueTask<EtlConnectorHttpSourceFetchResult> CaptureConnectorHttpSourceAsync(EtlConnectorPartitionLease lease,
        EtlHttpSourceFetchAttempt attempt, EtlRetainedHttpPage page, DateTimeOffset responseReceivedAtUtc,
        CancellationToken cancellationToken = default)
    {
        PrepareOperation(nameof(CaptureConnectorHttpSourceAsync), lease?.Scope, lease?.RunId, cancellationToken);
        return RunOperationAsync(nameof(CaptureConnectorHttpSourceAsync), lease?.Scope, lease?.RunId,
            () => CaptureConnectorHttpSourceOnceAsync(lease, attempt, page, responseReceivedAtUtc, cancellationToken), cancellationToken);
    }

    private ValueTask<EtlConnectorHttpSourceFetchResult> CaptureConnectorHttpSourceOnceAsync(EtlConnectorPartitionLease? lease,
        EtlHttpSourceFetchAttempt attempt, EtlRetainedHttpPage page, DateTimeOffset responseReceivedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return MutateConnectorHttpSourceAsync(lease, (fetch, _, policy, now) => fetch is null
            ? new(EtlStoreStatus.Conflict, null, null)
            : EtlHttpSourceFetchTransitions.Capture(fetch, attempt, page, responseReceivedAtUtc, now, policy), cancellationToken);
    }

    public async ValueTask<EtlHttpCaughtUpResult> RecordConnectorHttpCaughtUpAsync(EtlConnectorPartitionLease lease,
        EtlHttpSourceFetchAttempt attempt, DateTimeOffset responseReceivedAtUtc, CancellationToken cancellationToken = default)
    {
        PrepareOperation(nameof(RecordConnectorHttpCaughtUpAsync), lease?.Scope, lease?.RunId, cancellationToken);
        return await RunOperationAsync(nameof(RecordConnectorHttpCaughtUpAsync), lease?.Scope, lease?.RunId,
            () => RecordConnectorHttpCaughtUpOnceAsync(lease, attempt, responseReceivedAtUtc, cancellationToken), cancellationToken);
    }

    private async ValueTask<EtlHttpCaughtUpResult> RecordConnectorHttpCaughtUpOnceAsync(EtlConnectorPartitionLease? lease,
        EtlHttpSourceFetchAttempt attempt, DateTimeOffset responseReceivedAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(attempt);
        var key = RunKey(lease.Scope, lease.RunId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await FindRunRowAsync(lease.Scope, lease.RunId, cancellationToken);
        if (run is null) return new(EtlStoreStatus.NotFound, false);
        if (run.Status >= (int)EtlRunStatus.Succeeded) return new(EtlStoreStatus.Terminal, false);
        var connector = await ConnectorRowAsync(key, cancellationToken);
        if (connector is null) return new(EtlStoreStatus.Invalid, false);
        var partition = await db.ConnectorPartitions.SingleOrDefaultAsync(row => row.ScopeKey == key.ScopeKey &&
            row.RunKey == key.RunKey && row.PartitionOrdinal == lease.PartitionOrdinal, cancellationToken);
        if (partition is null) return new(EtlStoreStatus.NotFound, false);
        if (run.Status != (int)EtlRunStatus.Running || !HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks()))
            return new(EtlStoreStatus.LeaseLost, false);
        var invocation = EtlRuntimeRecordCodec.ReadInvocation(run.InvocationJson);
        var state = await ReadConnectorStateAsync(invocation, connector, cancellationToken);
        if (state.Pipeline.Continuous is not { } continuous) return new(EtlStoreStatus.Invalid, false);
        var position = await ReadConnectorPositionAsync(partition, state.Pipeline, state.Bindings, state.Prepared, cancellationToken);
        if (position != lease.Position || position is not EtlConnectorHttpPosition { Exhausted: false, Page: null } current ||
            partition.OwedBatchId is not null)
            return new(EtlStoreStatus.Conflict, false);
        var request = await HttpRequestForUpdateAsync(partition, current.PageSequence, cancellationToken);
        if (ReadConnectorHttpSource(request, state.Pipeline.Http) is not { } fetch) return new(EtlStoreStatus.Conflict, false);
        var now = NowTicks();
        var status = EtlHttpSourceFetchTransitions.CaughtUp(fetch, attempt, responseReceivedAtUtc, lease.Fence,
            EtlRuntimeRecordCodec.ReadUtc(now), state.Pipeline.Http);
        if (status != EtlStoreStatus.Applied) return new(status, false);
        // A request made at or before the attempt started covers this response: the caller retains it as the final page.
        if (connector.CompletionRequestedAtUtcTicks is { } requested && requested <= attempt.StartedAt.UtcTicks)
            return new(EtlStoreStatus.Applied, true);

        // The request returns to its never-fetched state: nothing about an empty, unmoved response needs retaining.
        var metadataRelease = checked(request.FetchMetadataBytes + request.SourceMetadataReserveBytes);
        var inputRelease = request.InputReservationBytes;
        request.FetchJson = null;
        request.FetchDigest = null;
        request.FetchMetadataBytes = 0;
        request.SourceMetadataReserveBytes = 0;
        request.InputReservationBytes = 0;
        request.MetadataBytes = checked(request.MetadataBytes - metadataRelease);
        partition.MetadataBytes = checked(partition.MetadataBytes - metadataRelease);
        partition.SourceInputReservedBytes = checked(partition.SourceInputReservedBytes - inputRelease);
        // The next poll publishes under a new page-cache key, so this poll's stalled attempts can never collide with it.
        partition.PollGeneration = checked(partition.PollGeneration + 1);
        // Completion requested after this attempt started: poll again at once so later records are not missed.
        partition.NotBeforeUtcTicks = connector.CompletionRequestedAtUtcTicks is null
            ? checked(now + TimeSpan.FromSeconds(continuous.PollIntervalSeconds).Ticks)
            : null;
        partition.Revision = checked(partition.Revision + 1);
        connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes - metadataRelease);
        connector.SourceInputReservedBytes = checked(connector.SourceInputReservedBytes - inputRelease);
        connector.Revision = checked(connector.Revision + 1);
        run.UpdatedAtUtcTicks = now;
        run.Revision = checked(run.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (!HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks())) return new(EtlStoreStatus.LeaseLost, false);
        await tx.CommitAsync(cancellationToken);
        return new(EtlStoreStatus.Applied, false);
    }

    private async ValueTask<EtlConnectorHttpSourceFetchResult> MutateConnectorHttpSourceAsync(EtlConnectorPartitionLease lease,
        Func<EtlConnectorHttpSourceFetch?, EtlConnectorHttpPosition, EtlHttpPolicy, DateTimeOffset, EtlConnectorHttpSourceFetchResult> transition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
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
        if (position != lease.Position || position is not EtlConnectorHttpPosition { Exhausted: false, Page: null } current ||
            partition.OwedBatchId is not null)
            return new(EtlStoreStatus.Conflict, null, null);
        var request = await HttpRequestForUpdateAsync(partition, current.PageSequence, cancellationToken);
        var previous = ReadConnectorHttpSource(request, state.Pipeline.Http);
        var result = transition(previous, current, state.Pipeline.Http, EtlRuntimeRecordCodec.ReadUtc(NowTicks()));
        if (!HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks())) return new(EtlStoreStatus.LeaseLost, null, null);
        if (result.Status != EtlStoreStatus.Applied)
            return result.Permit is { } replay && replay.ExpiresAt.UtcTicks <= NowTicks()
                ? new(EtlStoreStatus.Conflict, previous, null) : result;
        var fetch = result.Fetch ?? throw new InvalidDataException("An applied source transition omitted its journal.");
        if (fetch.Capture is { Page: var page })
        {
            try
            {
                var attached = current with { Page = page };
                EtlConnectorRecordCodec.Complete(attached);
                EtlConnectorRecordCodec.ValidateHttpPosition(attached, state.Pipeline.Http);
                if (state.Pipeline.Source is EtlHttpJsonSource { Pagination: EtlSinglePagePagination } && page.NextContinuation is not null ||
                    page.NextContinuation is not null && page.PageSequence + 1 >= state.Pipeline.Http.MaxPagesPerPartition)
                    return new(EtlStoreStatus.Invalid, previous, null);
                if (page.NextContinuation is { } continuation)
                {
                    var nextHash = EtlRuntimeIdentity.HttpRequestHash(current.Request with { Continuation = continuation });
                    if (await Requests(partition).AnyAsync(row => row.RequestHash == nextHash, cancellationToken))
                        return new(EtlStoreStatus.Invalid, previous, null);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or OverflowException)
            { return new(EtlStoreStatus.Invalid, previous, null); }
        }
        string json;
        long fetchBytes, sourceReserve;
        try
        {
            json = EtlConnectorRecordCodec.Write(fetch);
            fetchBytes = checked(MetadataBytes(json) + HttpSourceOutcomeMetadataReserve(fetch, state.Pipeline.Http));
            sourceReserve = fetch.Capture is { Page: var captured }
                ? HttpSourceCapturedPageProgressMetadataReserve(current, captured, state.Pipeline.Http)
                : HttpSourceCaptureProgressMetadataReserve(current, state.Pipeline.Http);
            if (checked(fetchBytes + (fetch.Capture is null ? HttpSourceCaptureJournalMetadataReserve(current, state.Pipeline.Http) : 0)) >
                EtlConnectorRecordCodec.MaximumDocumentBytes)
                return new(EtlStoreStatus.PermanentBudgetExceeded, previous, null);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        { return new(EtlStoreStatus.PermanentBudgetExceeded, previous, null); }
        catch (InvalidDataException)
        { return new(EtlStoreStatus.Invalid, previous, null); }
        var inputReserve = fetch.Capture?.Page.Body.ByteLength ?? state.Pipeline.Http.MaxPageBytes;
        var inputDelta = checked(inputReserve - request.InputReservationBytes);
        var metadataDelta = checked(fetchBytes + sourceReserve - request.FetchMetadataBytes - request.SourceMetadataReserveBytes);
        if (inputReserve > state.Pipeline.Http.MaxRetainedInputBytes - connector.RetainedInputBytes)
            return new(EtlStoreStatus.PermanentBudgetExceeded, previous, null);
        if (metadataDelta > state.Pipeline.Http.MaxRetainedMetadataBytes - connector.RetainedMetadataBytes)
            return new(await ConnectorMetadataBudgetStatusAsync(connector, state.Pipeline.Http, partition.PartitionOrdinal,
                checked(MetadataBytes(partition.PositionJson) + fetchBytes + sourceReserve), cancellationToken), previous, null);
        if (inputDelta > state.Pipeline.Http.MaxRetainedInputBytes - connector.RetainedInputBytes - connector.SourceInputReservedBytes)
            return new(EtlStoreStatus.BudgetExceeded, previous, null);
        request.FetchJson = json;
        request.FetchDigest = MetadataDigest(json);
        request.FetchMetadataBytes = fetchBytes;
        request.SourceMetadataReserveBytes = sourceReserve;
        request.InputReservationBytes = inputReserve;
        request.MetadataBytes = checked(request.MetadataBytes + metadataDelta);
        partition.MetadataBytes = checked(partition.MetadataBytes + metadataDelta);
        partition.SourceInputReservedBytes = checked(partition.SourceInputReservedBytes + inputDelta);
        partition.NotBeforeUtcTicks = fetch.NotBefore?.UtcTicks;
        partition.Revision = checked(partition.Revision + 1);
        connector.RetainedMetadataBytes = checked(connector.RetainedMetadataBytes + metadataDelta);
        connector.SourceInputReservedBytes = checked(connector.SourceInputReservedBytes + inputDelta);
        connector.Revision = checked(connector.Revision + 1);
        run.UpdatedAtUtcTicks = NowTicks();
        run.Revision = checked(run.Revision + 1);
        await db.SaveChangesAsync(cancellationToken);
        if (!HasConnectorAuthority(partition, lease.Owner, lease.Fence, NowTicks())) return new(EtlStoreStatus.LeaseLost, null, null);
        if (result.Permit is { } permit && permit.ExpiresAt.UtcTicks <= NowTicks()) return new(EtlStoreStatus.Conflict, null, null);
        await tx.CommitAsync(cancellationToken);
        return result;
    }
}
