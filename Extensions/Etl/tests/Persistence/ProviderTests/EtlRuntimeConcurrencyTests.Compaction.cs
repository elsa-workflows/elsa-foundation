using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Tests;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Elsa.Etl.Tests.EtlRuntimeStoreContract;

namespace Elsa.Etl.Persistence.ProviderTests;

public abstract partial class EtlRuntimeConcurrencyTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Continuous_compaction_retries_or_rolls_back_every_set_write_before_the_claim_save(bool exhaust)
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var clock = new MutableTimeProvider();
        var admission = await SeedContinuousHistoryAsync(database, clock);
        var fault = new CompactionCommandFault { RemainingFailures = exhaust ? int.MaxValue : 1 };
        await using var context = database.CreateContext(fault);
        var store = ((IEtlRuntimeTestDatabase)database).CreateStore(context, clock);
        var before = await ReadCompactionStateAsync(context);
        var expected = await CaptureCompactionExpectationAsync(context);
        EtlConnectorPartitionLease lease;
        if (exhaust)
        {
            var failure = await Assert.ThrowsAsync<EtlStoreException>(async () => await store.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "replacement"));
            Assert.Equal(nameof(store.ClaimConnectorPartitionAsync), failure.Operation);
            Assert.Equal(admission.Invocation.Scope, failure.Scope);
            Assert.Equal(admission.Invocation.RunId, failure.RunId);
            Assert.Same(fault.Failure, failure.InnerException);
            Assert.Equal(EfWriteRetry.DefaultMaxAttempts, fault.CompletedDeletes);
            Assert.Empty(context.ChangeTracker.Entries());
            await using (var observer = database.CreateContext())
                Assert.Equal(before, await ReadCompactionStateAsync(observer));
            fault.RemainingFailures = 0;
            lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "replacement"));
        }
        else
        {
            lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "replacement"));
            Assert.Equal(1, fault.CompletedDeletes);
        }
        await AssertCompactedHistoryAsync(database, admission, lease, clock, expected);
    }

    [SkippableFact]
    public async Task Competing_continuous_claims_compact_once_with_a_command_barrier()
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var clock = new MutableTimeProvider();
        var admission = await SeedContinuousHistoryAsync(database, clock);
        CompactionExpectation expected;
        await using (var observer = database.CreateContext())
            expected = await CaptureCompactionExpectationAsync(observer);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var gate = new FirstSaveGate();
        var conflicts = new ConcurrentQueue<string>();
        await using var left = database.CreateContext(new CompactionCommandGate(gate), new NativeRaceCommand(conflicts));
        await using var right = database.CreateContext(new CompactionCommandGate(gate), new NativeRaceCommand(conflicts));
        var a = ((IEtlRuntimeTestDatabase)database).CreateStore(left, clock).ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "left", cancellation.Token).AsTask();
        var b = ((IEtlRuntimeTestDatabase)database).CreateStore(right, clock).ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "right", cancellation.Token).AsTask();

        await ObserveRaceAsync(Task.WhenAll(a, b), conflicts, cancellation);

        var lease = Assert.Single(new[] { await a, await b }, item => item is not null)!;
        await AssertCompactedHistoryAsync(database, admission, lease, clock, expected);
    }

    private static async Task<EtlConnectorRunAdmission> SeedContinuousHistoryAsync(IEtlRuntimeTestDatabase database, TimeProvider clock)
    {
        await using var context = database.CreateContext();
        var store = database.CreateStore(context, clock);
        var pipeline = EtlV2TestPipelines.Create() with { Continuous = new EtlContinuousRunPolicy(1) };
        var admission = EtlRuntimeFixtures.ConnectorAdmission("compaction", new EtlScope("tenant-race", "compaction"), pipeline, ["single"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "seed"));
        var prepared = (await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId))!.Prepared!;
        for (var sequence = 0; sequence < 3; sequence++)
        {
            var attached = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, $"page-{sequence}", 10, 3, $"cursor-{sequence}"));
            Assert.Equal(EtlStoreStatus.Applied, attached.Status);
            lease = attached.Lease!;
            var batch = HttpAcceptanceBatch(admission, prepared, lease, 0, 3, $"batch-{sequence}");
            Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(lease, batch)).Status);
            var main = OutputRoute(admission, batch, EtlOutputRoute.Main, EtlConnectorKind.Http, $"main-{sequence}", EtlRuntimeFixtures.Hash, 17, 1);
            var errors = OutputRoute(admission, batch, EtlOutputRoute.Error, EtlConnectorKind.Http, $"errors-{sequence}", EtlRuntimeFixtures.Hash, 19, 1);
            Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(lease, batch.BatchId, new(1, 1, 1, main, errors))).Status);
            await AcknowledgeHttpRouteAsync(store, lease, batch.BatchId, EtlOutputRoute.Main, 700 + sequence * 2);
            await AcknowledgeHttpRouteAsync(store, lease, batch.BatchId, EtlOutputRoute.Error, 701 + sequence * 2);
            var accepted = await store.AcceptConnectorBatchAsync(lease, batch.BatchId);
            Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
            lease = accepted.Lease!;
        }
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
        Assert.Equal(3, await context.ConnectorHttpPages.CountAsync());
        Assert.Equal(4, await context.ConnectorHttpRequests.CountAsync());
        Assert.Equal(3, await context.ConnectorBatches.CountAsync());
        Assert.Equal(0, (await context.ConnectorPartitions.AsNoTracking().SingleAsync()).CompactedPageCount);
        return admission;
    }

    private static async Task<string> ReadCompactionStateAsync(EtlRuntimeDbContext context) => JsonSerializer.Serialize(new
    {
        Run = await context.Runs.AsNoTracking().SingleAsync(),
        Connector = await context.ConnectorRuns.AsNoTracking().SingleAsync(),
        Partitions = await context.ConnectorPartitions.AsNoTracking().OrderBy(row => row.PartitionOrdinal).ToListAsync(),
        Requests = await context.ConnectorHttpRequests.AsNoTracking().OrderBy(row => row.PageSequence).ToListAsync(),
        Pages = await context.ConnectorHttpPages.AsNoTracking().OrderBy(row => row.PageSequence).ToListAsync(),
        Batches = await context.ConnectorBatches.AsNoTracking().OrderBy(row => row.BatchOrdinal).ToListAsync(),
        Terminals = await context.Terminals.AsNoTracking().ToListAsync(),
        Outbox = await context.Outbox.AsNoTracking().ToListAsync()
    });

    private static async Task AssertCompactedHistoryAsync(IEtlRuntimeTestDatabase database,
        EtlConnectorRunAdmission admission, EtlConnectorPartitionLease lease, TimeProvider clock, CompactionExpectation expected)
    {
        await using var observer = database.CreateContext();
        var row = await observer.ConnectorPartitions.AsNoTracking().SingleAsync();
        Assert.Equal((2L, 2L, 2L), (row.CompactedPageCount, row.CompactedBatchCount, row.CompactedFilteredRecords));
        Assert.Equal((2L, 2L, 2L, 2L), (row.CompactedAcceptedRecords, row.CompactedRejectedRecords, row.CompactedMainEffects, row.CompactedErrorEffects));
        Assert.Equal((3L, 3L, 3L), (row.PageCount, row.AcceptedBatchCount, row.FilteredRecords));
        Assert.Equal(3, row.CurrentRequestSequence);
        Assert.Equal(10, row.PageBytes);
        Assert.Equal(expected.PartitionMetadataBytes, row.MetadataBytes);
        Assert.Equal(expected.BatchMetadataBytes, row.BatchMetadataBytes);
        Assert.Equal(EtlRuntimeRecordCodec.EncodeIdentity(lease.Owner, "worker id"), row.Owner);
        Assert.Equal(lease.Fence, row.Fence);
        Assert.Equal(lease.ExpiresAt.UtcTicks, row.ExpiresAtUtcTicks);
        var page = Assert.Single(await observer.ConnectorHttpPages.AsNoTracking().ToListAsync());
        var batch = Assert.Single(await observer.ConnectorBatches.AsNoTracking().ToListAsync());
        var requests = await observer.ConnectorHttpRequests.AsNoTracking().OrderBy(request => request.PageSequence).ToListAsync();
        Assert.Equal(expected.Page, JsonSerializer.Serialize(page));
        Assert.Equal(expected.Batch, JsonSerializer.Serialize(batch));
        Assert.Equal(expected.Requests, requests.Select(request => JsonSerializer.Serialize(request)).ToArray());
        Assert.Equal(2, page.PageSequence);
        Assert.Equal(3, page.AcceptedItemCount);
        Assert.Equal(2, batch.HttpPageSequence);
        Assert.True(batch.Settled);
        Assert.Equal(new long[] { 2, 3 }, requests.Select(request => request.PageSequence));
        Assert.Null(requests[0].PreviousPageSequence);
        Assert.Equal(2, requests[1].PreviousPageSequence);
        Assert.Equal(page.NextRequestHash, requests[1].RequestHash);
        Assert.Equal(requests[1].RequestHash, row.CurrentRequestHash);
        var connector = await observer.ConnectorRuns.AsNoTracking().SingleAsync();
        Assert.Equal(row.PageBytes, connector.RetainedInputBytes);
        Assert.Equal(expected.ConnectorMetadataBytes, connector.RetainedMetadataBytes);
        Assert.Equal(0, connector.SourceInputReservedBytes);
        Assert.Equal(36, connector.RetainedOutputBytes);
        Assert.Equal(36, row.RetainedOutputBytes);
        var runtime = await observer.Runs.AsNoTracking().SingleAsync();
        Assert.Equal((int)EtlRunStatus.Running, runtime.Status);
        Assert.Equal((3L, 3L, 3L, 0L, 0L), (runtime.AcceptedRecords, runtime.RejectedRecords, runtime.FilteredRecords, runtime.OutstandingRecords, runtime.OutstandingBytes));
        Assert.Empty(await observer.Terminals.AsNoTracking().ToListAsync());
        Assert.Empty(await observer.Outbox.AsNoTracking().ToListAsync());
        var inspection = Assert.IsType<EtlConnectorRunInspection>(await database.CreateStore(observer, clock).FindConnectorInspectionAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        Assert.Equal((3L, 3L), (inspection.AcceptedMainEffects, inspection.AcceptedErrorEffects));
    }

    private sealed record CompactionExpectation(string Page, string Batch, string[] Requests,
        long PartitionMetadataBytes, long BatchMetadataBytes, long ConnectorMetadataBytes);

    private static async Task<CompactionExpectation> CaptureCompactionExpectationAsync(EtlRuntimeDbContext context)
    {
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();
        var connector = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        var pages = await context.ConnectorHttpPages.AsNoTracking().ToListAsync();
        var batches = await context.ConnectorBatches.AsNoTracking().ToListAsync();
        var requests = await context.ConnectorHttpRequests.AsNoTracking().OrderBy(row => row.PageSequence).ToListAsync();
        var removedMetadata = checked(pages.Where(row => row.PageSequence < 2).Sum(row => row.MetadataBytes) +
            requests.Where(row => row.PageSequence < 2).Sum(row => row.MetadataBytes) +
            batches.Where(row => row.HttpPageSequence < 2).Sum(row => row.MetadataBytes));
        Assert.True(removedMetadata > 0);
        var retainedRequests = requests.Where(row => row.PageSequence >= 2).ToArray();
        // Only these two base-predecessor columns intentionally change on retained history.
        retainedRequests[0].PreviousPageSequence = null;
        retainedRequests[0].PreviousHasContinuation = null;
        return new(JsonSerializer.Serialize(Assert.Single(pages, row => row.PageSequence == 2)),
            JsonSerializer.Serialize(Assert.Single(batches, row => row.HttpPageSequence == 2)),
            retainedRequests.Select(row => JsonSerializer.Serialize(row)).ToArray(),
            checked(partition.MetadataBytes - removedMetadata),
            batches.Where(row => row.HttpPageSequence >= 2).Sum(row => row.MetadataBytes),
            checked(connector.RetainedMetadataBytes - removedMetadata));
    }

    private sealed class CompactionCommandFault : DbCommandInterceptor
    {
        public int RemainingFailures { get; set; }
        public int CompletedDeletes { get; private set; }
        public Exception Failure { get; } = new DbUpdateConcurrencyException("Controlled failure after compaction set writes.");

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (RemainingFailures <= 0 || !IsCompactionCommand(command, "DELETE")) return ValueTask.FromResult(result);
            Assert.Equal(2, result);
            Assert.NotNull(eventData.Context!.Database.CurrentTransaction);
            CompletedDeletes++;
            RemainingFailures--;
            throw Failure;
        }
    }

    private sealed class CompactionCommandGate(FirstSaveGate gate) : DbCommandInterceptor
    {
        private int joined;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (IsCompactionCommand(command, "UPDATE") && Interlocked.Exchange(ref joined, 1) == 0)
                await gate.ArriveAsync(cancellationToken);
            return result;
        }
    }

    private static bool IsCompactionCommand(DbCommand command, string verb) =>
        command.CommandText.TrimStart().StartsWith(verb, StringComparison.OrdinalIgnoreCase) &&
        command.CommandText.Contains(EtlRuntimeEfModule.ConnectorHttpRequestTableName, StringComparison.OrdinalIgnoreCase);
}
