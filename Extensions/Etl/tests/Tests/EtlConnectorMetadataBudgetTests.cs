using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Http_request_ledger_and_metadata_counters_match_persisted_utf8_documents_and_replays()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("metadata-ledger", new("tenant", "metadata-ledger"), httpShardIds: ["shard"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var initial = Assert.IsType<EtlConnectorHttpPosition>(lease.Position);
        var initialRequestHash = EtlRuntimeIdentity.HttpRequestHash(initial.Request);
        var seed = Assert.Single(await context.ConnectorHttpRequests.AsNoTracking().ToListAsync());

        Assert.Equal(0L, seed.PageSequence);
        Assert.Equal(initialRequestHash, seed.RequestHash);
        Assert.Null(seed.PreviousPageSequence);
        Assert.Null(seed.PreviousHasContinuation);
        Assert.Equal(initial.Request, EtlConnectorRecordCodec.Read<EtlHttpRequest>(seed.RequestJson));
        var beforeAttach = await AssertMetadataAccountingAsync(context);

        const string firstContinuation = "cursor-雪-α";
        var firstPage = RetainedPage(lease, "page-0", 10, 0, firstContinuation);
        var attached = await store.AttachTestHttpPageAsync(lease, firstPage);
        var attachedLease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var afterFirstAttach = await AssertMetadataAccountingAsync(context);
        var replay = await store.AttachTestHttpPageAsync(attachedLease, firstPage);
        var replayFromOldLease = await store.AttachTestHttpPageAsync(lease, firstPage);
        var afterAttach = await AssertMetadataAccountingAsync(context);
        var page0 = await context.ConnectorHttpPages.AsNoTracking().SingleAsync(page => page.PageSequence == 0);

        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        Assert.Equal(EtlStoreStatus.Replay, replayFromOldLease.Status);
        Assert.Equal(afterFirstAttach, afterAttach);
        Assert.Equal(1, await context.ConnectorHttpRequests.CountAsync());
        Assert.Equal(EtlRuntimeIdentity.HttpRequestHash(initial.Request with { Continuation = firstContinuation }), page0.NextRequestHash);
        Assert.True(page0.HasContinuation);
        Assert.True(afterAttach > beforeAttach);

        var advanced = await store.AdvanceEmptyHttpPageAsync(attachedLease);
        var advancedLease = Assert.IsType<EtlConnectorPartitionLease>(advanced.Lease);
        var afterAdvance = await AssertMetadataAccountingAsync(context);
        var advanceReplay = await store.AdvanceEmptyHttpPageAsync(attachedLease);
        var afterAdvanceReplay = await AssertMetadataAccountingAsync(context);
        var requests = await context.ConnectorHttpRequests.AsNoTracking().OrderBy(request => request.PageSequence).ToListAsync();

        Assert.Equal(EtlStoreStatus.Applied, advanced.Status);
        Assert.Equal(EtlStoreStatus.Replay, advanceReplay.Status);
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests.Count, requests.Select(request => request.RequestHash).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1L, requests[1].PageSequence);
        Assert.Equal(0L, requests[1].PreviousPageSequence);
        Assert.True(requests[1].PreviousHasContinuation is true);
        Assert.Equal(EtlRuntimeIdentity.HttpRequestHash(new EtlHttpRequest("shard", firstContinuation)), requests[1].RequestHash);
        Assert.Equal(new EtlHttpRequest("shard", firstContinuation), EtlConnectorRecordCodec.Read<EtlHttpRequest>(requests[1].RequestJson));
        Assert.Equal(requests[1].RequestHash, page0.NextRequestHash);
        Assert.Equal(afterAdvance, afterAdvanceReplay);

        const string secondContinuation = "cursor-二";
        var secondPage = RetainedPage(advancedLease, "page-1", 12, 0, secondContinuation);
        var attachedSecond = await store.AttachTestHttpPageAsync(advancedLease, secondPage);
        Assert.Equal(EtlStoreStatus.Applied, attachedSecond.Status);
        await AssertMetadataAccountingAsync(context);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(attachedSecond.Lease!));

        await using var recoveryContext = database.CreateContext();
        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await database.CreateStore(recoveryContext)
            .ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "replacement"));
        var recoveredPosition = Assert.IsType<EtlConnectorHttpPosition>(recovered.Position);
        var page1 = await recoveryContext.ConnectorHttpPages.AsNoTracking().SingleAsync(page => page.PageSequence == 1);
        var currentLedger = await recoveryContext.ConnectorHttpRequests.AsNoTracking().SingleAsync(request => request.PageSequence == 1);

        Assert.Equal(1L, recoveredPosition.PageSequence);
        Assert.Equal(secondContinuation, recoveredPosition.Page!.NextContinuation);
        Assert.Equal(0L, page0.PageSequence);
        Assert.Equal(currentLedger.PageSequence - 1, page0.PageSequence);
        Assert.True(page0.HasContinuation);
        Assert.Equal(currentLedger.RequestHash, page0.NextRequestHash);
        Assert.Equal(recoveredPosition.Request, EtlConnectorRecordCodec.Read<EtlHttpRequest>(currentLedger.RequestJson));
        Assert.Equal(EtlRuntimeIdentity.HttpRequestHash(recoveredPosition.Request), currentLedger.RequestHash);
        Assert.Equal(EtlRuntimeIdentity.HttpRequestHash(recoveredPosition.Request), page1.RequestHash);
        Assert.Equal(2, await recoveryContext.ConnectorHttpRequests.CountAsync());
        Assert.Equal(2, await recoveryContext.ConnectorHttpPages.CountAsync());
        await AssertMetadataAccountingAsync(recoveryContext);
    }

    [Fact]
    public async Task Tiny_metadata_limit_refuses_preparation_without_persisting_partition_or_request_rows()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with { Http = defaults.Http with { MaxRetainedMetadataBytes = 1 } };
        var admission = ConnectorAdmission("metadata-preparation-limit", new("tenant", "metadata-preparation-limit"), pipeline, ["shard"]);

        var admitted = await store.AdmitConnectorAsync(admission);
        Assert.Equal(0, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes);
        Assert.Empty(await context.ConnectorPartitions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.ConnectorHttpRequests.AsNoTracking().ToListAsync());
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(
            admission.Invocation.Scope, "preparer"));
        var status = await store.CommitConnectorPreparationAsync(preparation, new(null, null));

        Assert.Equal(EtlStoreStatus.Applied, admitted.Status);
        Assert.Equal(EtlStoreStatus.PermanentBudgetExceeded, status);
        Assert.Empty(await context.ConnectorPartitions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.ConnectorHttpRequests.AsNoTracking().ToListAsync());
        Assert.Empty(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());
        var connector = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        Assert.Null(connector.PreparedJson);
        Assert.Equal(0L, connector.RetainedMetadataBytes);
        Assert.Equal(0L, await AssertMetadataAccountingAsync(context));
    }

    [Fact]
    public async Task Begin_refuses_source_reservation_when_only_prepared_metadata_fits()
    {
        const string runId = "metadata-begin-limit";
        const string partitionId = "metadata-begin-limit";
        var limit = await MeasurePreparedMetadataBudgetAsync(runId, partitionId);

        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var store = database.CreateStore(context, clock);
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with { Http = defaults.Http with { MaxRetainedMetadataBytes = limit } };
        var admission = ConnectorAdmission(runId, new("tenant", partitionId), pipeline, ["shard"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var beforeBegin = await AssertMetadataAccountingAsync(context);
        var priorPartition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();
        var priorRequest = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync();
        var priorRun = await context.ConnectorRuns.AsNoTracking().SingleAsync();

        var begin = await store.BeginConnectorHttpSourceAsync(lease, "00000000000000000000000000000001");
        context.ChangeTracker.Clear();

        Assert.Equal(limit, beforeBegin);
        Assert.Equal(EtlStoreStatus.PermanentBudgetExceeded, begin.Status);
        Assert.Null(begin.Permit);
        Assert.Null(begin.Fetch);
        var unchangedPartition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();
        var unchangedRequest = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync();
        var unchangedRun = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        Assert.Equal(priorPartition.PositionJson, unchangedPartition.PositionJson);
        Assert.Equal(priorPartition.MetadataBytes, unchangedPartition.MetadataBytes);
        Assert.Equal(0, unchangedPartition.PageCount);
        Assert.Equal(priorRequest.RequestJson, unchangedRequest.RequestJson);
        Assert.Null(unchangedRequest.FetchJson);
        Assert.Null(unchangedRequest.FetchDigest);
        Assert.Equal(0, unchangedRequest.FetchMetadataBytes);
        Assert.Equal(0, unchangedRequest.SourceMetadataReserveBytes);
        Assert.Equal(0, unchangedRequest.InputReservationBytes);
        Assert.Equal(priorRun.RetainedMetadataBytes, unchangedRun.RetainedMetadataBytes);
        Assert.Equal(0, unchangedRun.SourceInputReservedBytes);
        Assert.Empty(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());
        Assert.Single(await context.ConnectorHttpRequests.AsNoTracking().ToListAsync());
        Assert.Equal(beforeBegin, await AssertMetadataAccountingAsync(context));
    }

    [Fact]
    public async Task Http_metadata_budget_is_shared_across_shards()
    {
        const string firstRun = "metadata-cross-shard-limit";
        var limits = await MeasureTwoShardBeginBudgetsAsync(firstRun, "metadata-cross-shard-limit");
        Assert.True(limits.AfterSecondBegin > limits.AfterFirstBegin);

        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var store = database.CreateStore(context, clock);
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with { Http = defaults.Http with { MaxRetainedMetadataBytes = limits.AfterFirstBegin } };
        var admission = ConnectorAdmission(firstRun, new("tenant", "metadata-cross-shard-limit"), pipeline, ["shard-a", "shard-b"]);
        var leases = await StartHttpRunAsync(store, admission, "worker");
        var first = await store.BeginConnectorHttpSourceAsync(leases[0], "00000000000000000000000000000001");
        var afterFirst = await AssertMetadataAccountingAsync(context);
        var second = await store.BeginConnectorHttpSourceAsync(leases[1], "00000000000000000000000000000002");
        context.ChangeTracker.Clear();

        Assert.Equal(EtlStoreStatus.Applied, first.Status);
        Assert.Equal(limits.AfterFirstBegin, afterFirst);
        Assert.NotNull(first.Permit);
        Assert.Equal(EtlStoreStatus.BudgetExceeded, second.Status);
        Assert.Null(second.Permit);
        Assert.Null(second.Fetch);
        Assert.Equal(afterFirst, await AssertMetadataAccountingAsync(context));
        Assert.Empty(await context.ConnectorHttpPages.ToListAsync());
        Assert.Equal(2, await context.ConnectorHttpRequests.CountAsync());
        var requests = await context.ConnectorHttpRequests.AsNoTracking().OrderBy(request => request.PartitionOrdinal).ToListAsync();
        Assert.NotNull(requests[0].FetchJson);
        Assert.True(requests[0].SourceMetadataReserveBytes > 0);
        Assert.Null(requests[1].FetchJson);
        Assert.Equal(0, requests[1].InputReservationBytes);
        Assert.Equal(0, requests[1].FetchMetadataBytes);
        Assert.Equal(0, requests[1].SourceMetadataReserveBytes);
        var partitions = await context.ConnectorPartitions.AsNoTracking().OrderBy(partition => partition.PartitionOrdinal).ToListAsync();
        Assert.Equal(0L, partitions[0].PageCount);
        Assert.Equal(0L, partitions[1].PageCount);
        Assert.All(partitions, partition => Assert.True(partition.MetadataBytes < limits.AfterFirstBegin));
    }

    [Fact]
    public async Task Historical_http_fetch_journal_is_not_released_for_a_later_page_reservation()
    {
        const string runId = "metadata-source-history-limit";
        const string partitionId = "metadata-source-history-limit";
        var defaults = EtlV2TestPipelines.Create();
        var http = defaults.Http with { MaxPageBytes = 128, MaxContinuationBytes = 64 };
        var pipeline = defaults with { Http = http };
        var measured = await MeasureHistoricalHttpRequestMetadataBudgetAsync(runId, partitionId, pipeline);
        Assert.True(measured.HistoricalFetchBytes > 0);
        Assert.True(measured.AfterFirstPage < measured.AfterSecondBegin);

        pipeline = pipeline with { Http = http with { MaxRetainedMetadataBytes = measured.AfterSecondBegin - 1 } };
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission(runId, new("tenant", partitionId), pipeline, ["shard"]);
        var initial = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var first = await store.BeginConnectorHttpSourceAsync(initial, "00000000000000000000000000000001");
        Assert.Equal(EtlStoreStatus.Applied, first.Status);
        var page = RetainedPage(initial, "history-page-0", 10, 0, "cursor-1");
        Assert.Equal(EtlStoreStatus.Applied,
            (await store.CaptureConnectorHttpSourceAsync(initial, first.Permit!, page, first.Permit!.StartedAt)).Status);
        var attached = await store.AttachHttpPageAsync(initial, page);
        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        var advanced = await store.AdvanceEmptyHttpPageAsync(attached.Lease!);
        Assert.Equal(EtlStoreStatus.Applied, advanced.Status);
        var beforeSecondBegin = await AssertMetadataAccountingAsync(context);
        var historical = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync(request => request.PageSequence == 0);
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Captured,
            EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(historical.FetchJson!).Phase);
        Assert.Equal(measured.HistoricalFetchBytes, historical.FetchMetadataBytes);
        Assert.Equal(measured.AfterFirstPage, beforeSecondBegin);

        var second = await store.BeginConnectorHttpSourceAsync(advanced.Lease!, "00000000000000000000000000000002");

        Assert.Equal(EtlStoreStatus.PermanentBudgetExceeded, second.Status);
        Assert.Null(second.Permit);
        Assert.Equal(beforeSecondBegin, await AssertMetadataAccountingAsync(context));
        var connector = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        Assert.Equal(0, connector.SourceInputReservedBytes);
        Assert.Equal(2, await context.ConnectorHttpRequests.CountAsync());
        Assert.Null(await context.ConnectorHttpRequests.AsNoTracking()
            .Where(request => request.PageSequence == 1).Select(request => request.FetchJson).SingleAsync());
    }

    [Fact]
    public async Task Request_ledger_keys_cannot_be_rewritten_after_a_page_references_them()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("immutable-request-ledger", new("tenant", "immutable-request-ledger"), httpShardIds: ["shard"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var originalRequestHash = EtlRuntimeIdentity.HttpRequestHash(Assert.IsType<EtlConnectorHttpPosition>(lease.Position).Request);
        var attached = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "page-0", 10, 0, "cursor-1"));
        Assert.Equal(EtlStoreStatus.Applied, attached.Status);

        var request = await context.ConnectorHttpRequests.SingleAsync(row => row.PageSequence == 0);
        request.RequestHash = new string('d', 64);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        request = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync(row => row.PageSequence == 0);
        var tableName = $"\"{EtlRuntimeEfModule.ConnectorHttpRequestTableName}\"";
        var update = $"UPDATE {tableName} SET \"RequestHash\" = {{0}} WHERE \"ScopeKey\" = {{1}} AND \"RunKey\" = {{2}} AND \"PartitionOrdinal\" = {{3}} AND \"PageSequence\" = {{4}}";
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(update,
            new string('d', 64), request.ScopeKey, request.RunKey, request.PartitionOrdinal, request.PageSequence));

        Assert.Equal(originalRequestHash,
            (await context.ConnectorHttpRequests.AsNoTracking().SingleAsync(row => row.PageSequence == 0)).RequestHash);
        Assert.Equal(originalRequestHash,
            (await context.ConnectorHttpPages.AsNoTracking().SingleAsync(page => page.PageSequence == 0)).RequestHash);
    }

    [Theory]
    [InlineData("renew")]
    [InlineData("release")]
    [InlineData("attach")]
    [InlineData("advance")]
    public async Task Hot_http_transitions_use_bounded_indexed_lookups_after_history_grows(string operation)
    {
        await using var database = await TestDatabase.CreateAsync();
        EtlConnectorPartitionLease lease;
        await using (var seedContext = database.CreateContext())
        {
            var store = database.CreateStore(seedContext);
            var admission = ConnectorAdmission("metadata-hot-renew", new("tenant", "metadata-hot-renew"), httpShardIds: ["shard"]);
            lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
            for (var pageNumber = 0; pageNumber < 5; pageNumber++)
            {
                var attached = await store.AttachTestHttpPageAsync(lease,
                    RetainedPage(lease, $"page-{pageNumber}", 10, 0, $"cursor-{pageNumber + 1}"));
                lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
                lease = Assert.IsType<EtlConnectorPartitionLease>((await store.AdvanceEmptyHttpPageAsync(lease)).Lease);
            }

            if (operation == "advance")
            {
                var lastPage = await store.AttachTestHttpPageAsync(lease,
                    RetainedPage(lease, "hot-current-page", 10, 0, "cursor-6"));
                Assert.Equal(EtlStoreStatus.Applied, lastPage.Status);
                lease = Assert.IsType<EtlConnectorPartitionLease>(lastPage.Lease);
            }
        }

        var capture = new SqlCaptureInterceptor();
        await using var transitionContext = database.CreateContext(capture);
        var transitionStore = database.CreateStore(transitionContext);
        var status = operation switch
        {
            "renew" => await transitionStore.RenewConnectorPartitionAsync(lease),
            "release" => await transitionStore.ReleaseConnectorPartitionAsync(lease),
            "attach" => (await transitionStore.AttachTestHttpPageAsync(lease, RetainedPage(lease, "hot-new-page", 10, 0, "cursor-6"))).Status,
            "advance" => (await transitionStore.AdvanceEmptyHttpPageAsync(lease)).Status,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
        var historyQueries = capture.Commands.Where(command => command.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            (command.Contains(EtlRuntimeEfModule.ConnectorHttpPageTableName, StringComparison.OrdinalIgnoreCase) ||
             command.Contains(EtlRuntimeEfModule.ConnectorHttpRequestTableName, StringComparison.OrdinalIgnoreCase))).ToArray();

        Assert.Equal(EtlStoreStatus.Applied, status);
        Assert.NotEmpty(historyQueries);
        Assert.All(historyQueries, AssertBoundedHistoryLookup);
    }

    [Fact]
    public async Task Expired_http_page_attachment_rolls_back_metadata_and_ledger_accounting()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var expire = new ConnectorExpireAfterSave(clock);
        string priorPosition;
        long priorMetadataBytes;
        long priorInputBytes;
        string priorRequests;
        long priorInputReservation;
        EtlConnectorPositionResult expired;
        var admission = ConnectorAdmission("metadata-attach-expiry", new("tenant", "metadata-attach-expiry"), httpShardIds: ["shard"]);

        await using (var context = database.CreateContext(expire))
        {
            var store = database.CreateStore(context, clock);
            var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
            var page = RetainedPage(lease, "expired-page", 8, 0, "cursor-1");
            await CaptureHttpPageForAttachmentAsync(store, lease, page);
            priorPosition = await context.ConnectorPartitions.AsNoTracking().Select(partition => partition.PositionJson).SingleAsync();
            priorMetadataBytes = (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes;
            priorInputBytes = (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedInputBytes;
            priorInputReservation = (await context.ConnectorRuns.AsNoTracking().SingleAsync()).SourceInputReservedBytes;
            Assert.Equal(page.Body.ByteLength, priorInputReservation);
            priorRequests = System.Text.Json.JsonSerializer.Serialize(await context.ConnectorHttpRequests.AsNoTracking()
                .OrderBy(request => request.PageSequence).ToArrayAsync());

            expire.Armed = true;
            expired = await store.AttachHttpPageAsync(lease, page);
        }

        Assert.Equal(EtlStoreStatus.LeaseLost, expired.Status);
        Assert.Null(expired.Lease);
        await using var verificationContext = database.CreateContext();
        Assert.Equal(priorPosition,
            await verificationContext.ConnectorPartitions.AsNoTracking().Select(partition => partition.PositionJson).SingleAsync());
        Assert.Equal(priorMetadataBytes,
            (await verificationContext.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes);
        Assert.Equal(priorInputBytes,
            (await verificationContext.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedInputBytes);
        Assert.Equal(priorInputReservation,
            (await verificationContext.ConnectorRuns.AsNoTracking().SingleAsync()).SourceInputReservedBytes);
        Assert.Equal(priorInputReservation,
            (await verificationContext.ConnectorPartitions.AsNoTracking().SingleAsync()).SourceInputReservedBytes);
        Assert.Equal(priorRequests, System.Text.Json.JsonSerializer.Serialize(await verificationContext.ConnectorHttpRequests
            .AsNoTracking().OrderBy(request => request.PageSequence).ToArrayAsync()));
        Assert.Empty(await verificationContext.ConnectorHttpPages.AsNoTracking().ToListAsync());
        Assert.Equal(0L, (await verificationContext.ConnectorPartitions.AsNoTracking().SingleAsync()).PageCount);
        Assert.Equal(0L, (await verificationContext.ConnectorPartitions.AsNoTracking().SingleAsync()).PageBytes);
        Assert.Equal(priorMetadataBytes, await AssertMetadataAccountingAsync(verificationContext));
    }

    [Fact]
    public async Task Expired_http_page_advance_rolls_back_new_request_and_metadata_accounting()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var expire = new ConnectorExpireAfterSave(clock);
        string priorPosition;
        long priorMetadataBytes;
        long priorInputBytes;
        string[] priorRequests;
        EtlConnectorPositionResult expired;
        var admission = ConnectorAdmission("metadata-advance-expiry", new("tenant", "metadata-advance-expiry"), httpShardIds: ["shard"]);

        await using (var context = database.CreateContext(expire))
        {
            var store = database.CreateStore(context, clock);
            var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
            var attached = await store.AttachTestHttpPageAsync(lease,
                RetainedPage(lease, "page-before-expiry", 12, 0, "cursor-1"));
            Assert.Equal(EtlStoreStatus.Applied, attached.Status);
            var attachedLease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
            priorPosition = await context.ConnectorPartitions.AsNoTracking().Select(partition => partition.PositionJson).SingleAsync();
            priorMetadataBytes = (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes;
            priorInputBytes = (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedInputBytes;
            priorRequests = await context.ConnectorHttpRequests.AsNoTracking().OrderBy(request => request.PageSequence)
                .Select(request => request.RequestHash + ":" + request.RequestJson).ToArrayAsync();

            expire.Armed = true;
            expired = await store.AdvanceEmptyHttpPageAsync(attachedLease);
        }

        Assert.Equal(EtlStoreStatus.LeaseLost, expired.Status);
        Assert.Null(expired.Lease);
        await using var verificationContext = database.CreateContext();
        Assert.Equal(priorPosition,
            await verificationContext.ConnectorPartitions.AsNoTracking().Select(partition => partition.PositionJson).SingleAsync());
        Assert.Equal(priorMetadataBytes,
            (await verificationContext.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedMetadataBytes);
        Assert.Equal(priorInputBytes,
            (await verificationContext.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedInputBytes);
        Assert.Equal(priorRequests, await verificationContext.ConnectorHttpRequests.AsNoTracking().OrderBy(request => request.PageSequence)
            .Select(request => request.RequestHash + ":" + request.RequestJson).ToArrayAsync());
        Assert.Single(await verificationContext.ConnectorHttpPages.AsNoTracking().ToListAsync());
        var partition = await verificationContext.ConnectorPartitions.AsNoTracking().SingleAsync();
        Assert.Equal(1L, partition.PageCount);
        Assert.Equal(12L, partition.PageBytes);
        Assert.Equal(priorMetadataBytes, await AssertMetadataAccountingAsync(verificationContext));
    }

    private static async Task<long> MeasurePreparedMetadataBudgetAsync(string runId, string partitionId)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context, new MutableTimeProvider());
        var admission = ConnectorAdmission(runId, new("tenant", partitionId), httpShardIds: ["shard"]);
        await StartHttpRunAsync(store, admission, "worker");
        return await AssertMetadataAccountingAsync(context);
    }

    private static async Task<(long AfterFirstBegin, long AfterSecondBegin)> MeasureTwoShardBeginBudgetsAsync(
        string runId, string partitionId)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var store = database.CreateStore(context, clock);
        var admission = ConnectorAdmission(runId, new("tenant", partitionId), httpShardIds: ["shard-a", "shard-b"]);
        var leases = await StartHttpRunAsync(store, admission, "worker");
        Assert.Equal(EtlStoreStatus.Applied, (await store.BeginConnectorHttpSourceAsync(leases[0],
            "00000000000000000000000000000001")).Status);
        var afterFirst = await AssertMetadataAccountingAsync(context);
        Assert.Equal(EtlStoreStatus.Applied, (await store.BeginConnectorHttpSourceAsync(leases[1],
            "00000000000000000000000000000002")).Status);
        var afterSecond = await AssertMetadataAccountingAsync(context);
        return (afterFirst, afterSecond);
    }

    private static async Task<(long AfterFirstPage, long AfterSecondBegin, long HistoricalFetchBytes)>
        MeasureHistoricalHttpRequestMetadataBudgetAsync(string runId, string partitionId, EtlPipelineV2 pipeline)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission(runId, new("tenant", partitionId), pipeline, ["shard"]);
        var initial = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var first = await store.BeginConnectorHttpSourceAsync(initial, "00000000000000000000000000000001");
        Assert.Equal(EtlStoreStatus.Applied, first.Status);
        var page = RetainedPage(initial, "history-page-0", 10, 0, "cursor-1");
        Assert.Equal(EtlStoreStatus.Applied,
            (await store.CaptureConnectorHttpSourceAsync(initial, first.Permit!, page, first.Permit!.StartedAt)).Status);
        var attached = await store.AttachHttpPageAsync(initial, page);
        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        var advanced = await store.AdvanceEmptyHttpPageAsync(attached.Lease!);
        Assert.Equal(EtlStoreStatus.Applied, advanced.Status);
        var afterFirstPage = await AssertMetadataAccountingAsync(context);
        var historical = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync(request => request.PageSequence == 0);
        var second = await store.BeginConnectorHttpSourceAsync(advanced.Lease!, "00000000000000000000000000000002");
        Assert.Equal(EtlStoreStatus.Applied, second.Status);
        var afterSecondBegin = await AssertMetadataAccountingAsync(context);
        return (afterFirstPage, afterSecondBegin, historical.FetchMetadataBytes);
    }

    private static async Task<long> AssertMetadataAccountingAsync(EtlRuntimeSqliteDbContext context)
    {
        var partitions = await context.ConnectorPartitions.AsNoTracking().ToListAsync();
        var requests = await context.ConnectorHttpRequests.AsNoTracking().ToListAsync();
        var pages = await context.ConnectorHttpPages.AsNoTracking().ToListAsync();
        var actualRunBytes = partitions.Sum(partition => (long)Utf8Bytes(partition.PositionJson)) +
                             requests.Sum(request => (long)Utf8Bytes(request.RequestJson) + request.FetchMetadataBytes + request.SourceMetadataReserveBytes) +
                             pages.Sum(page => (long)Utf8Bytes(page.PageJson));
        var connector = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        Assert.Equal(actualRunBytes, connector.RetainedMetadataBytes);

        foreach (var partition in partitions)
        {
            var partitionRequests = requests.Where(request => request.PartitionOrdinal == partition.PartitionOrdinal).ToArray();
            var partitionPages = pages.Where(page => page.PartitionOrdinal == partition.PartitionOrdinal).ToArray();
            var actualPartitionBytes = (long)Utf8Bytes(partition.PositionJson) +
                                       partitionRequests.Sum(request => (long)Utf8Bytes(request.RequestJson) + request.FetchMetadataBytes + request.SourceMetadataReserveBytes) +
                                       partitionPages.Sum(page => (long)Utf8Bytes(page.PageJson));
            Assert.Equal(actualPartitionBytes, partition.MetadataBytes);
            Assert.Equal((long)partitionPages.Length, partition.PageCount);
            Assert.Equal(partitionPages.Sum(page => page.ByteLength), partition.PageBytes);
        }

        Assert.All(requests, request =>
        {
            Assert.Equal((long)Utf8Bytes(request.RequestJson) + request.FetchMetadataBytes + request.SourceMetadataReserveBytes, request.MetadataBytes);
            Assert.True(request.FetchMetadataBytes >= (request.FetchJson is null ? 0 : Utf8Bytes(request.FetchJson)));
        });
        Assert.All(pages, page => Assert.Equal((long)Utf8Bytes(page.PageJson), page.MetadataBytes));

        return actualRunBytes;
    }

    private static int Utf8Bytes(string value) => Encoding.UTF8.GetByteCount(value);

    private static void AssertBoundedHistoryLookup(string command)
    {
        Assert.True(command.Contains("WHERE", StringComparison.OrdinalIgnoreCase), command);
        Assert.True(command.Contains("PartitionOrdinal", StringComparison.OrdinalIgnoreCase), command);
        Assert.True(HasIndexedEquality(command, "PageSequence") || HasIndexedEquality(command, "RequestHash"), command);
        Assert.False(command.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase), command);
        Assert.False(command.Contains("SUM(", StringComparison.OrdinalIgnoreCase), command);
    }

    private static bool HasIndexedEquality(string command, string column)
    {
        var pattern = "(?:\"|`|\\[)?" + Regex.Escape(column) + "(?:\"|`|\\])?\\s*=\\s*[@:$]";
        return Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyList<string> Commands => _commands.ToArray();

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result) => Capture(command, result);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Capture(command, result));

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result) => Capture(command, result);

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Capture(command, result));

        private InterceptionResult<T> Capture<T>(DbCommand command, InterceptionResult<T> result)
        {
            _commands.Enqueue(command.CommandText);
            return result;
        }
    }
}
