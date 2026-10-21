using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Http_page_attachment_is_immutable_idempotent_and_position_guarded()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("http-page-immutable", new EtlScope("tenant-a", "http-page-immutable"), httpShardIds: ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var page = RetainedPage(lease, "page-0", byteLength: 31, recordCount: 2, nextContinuation: "cursor-1");

        var attached = await store.AttachTestHttpPageAsync(lease, page);
        var attachedLease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
        var replay = await store.AttachTestHttpPageAsync(attachedLease, page);
        var replayFromPreAttachLease = await store.AttachTestHttpPageAsync(lease, page);
        var changed = await store.AttachHttpPageAsync(attachedLease, page with
        {
            Body = page.Body with { Key = "different-page" }
        });
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));

        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        Assert.Equal(page, Assert.IsType<EtlConnectorHttpPosition>(attachedLease.Position).Page);
        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        Assert.Equal(attachedLease.Position, replay.Lease!.Position);
        Assert.Equal(EtlStoreStatus.Replay, replayFromPreAttachLease.Status);
        Assert.Equal(attachedLease.Position, replayFromPreAttachLease.Lease!.Position);
        Assert.Equal(EtlStoreStatus.Conflict, changed.Status);
        Assert.Null(changed.Lease);
        Assert.Equal(31, run.RetainedInputBytes);
        Assert.Single(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("records")]
    [InlineData("request-hash")]
    [InlineData("body-hash")]
    [InlineData("empty-continuation")]
    public async Task Http_page_attachment_rejects_invalid_metadata_without_retaining_it(string defect)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Http = defaults.Http with { MaxPageBytes = 32, MaxRetainedInputBytes = 64, MaxPageRecords = 2, MaxContinuationBytes = 8 }
        };
        var admission = ConnectorAdmission($"http-page-invalid-{defect}", new EtlScope("tenant-a", $"http-page-invalid-{defect}"), pipeline, ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var page = RetainedPage(lease, "invalid-page", byteLength: 10, recordCount: 1, nextContinuation: "cursor-1");
        page = defect switch
        {
            "bytes" => page with { Body = page.Body with { ByteLength = 33 } },
            "records" => page with { RecordCount = 3 },
            "request-hash" => page with { RequestHash = new string('b', 64) },
            "body-hash" => page with { Body = page.Body with { ContentHash = "not-a-hash" } },
            "empty-continuation" => page with { NextContinuation = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(defect), defect, null)
        };

        var result = await store.AttachTestHttpPageAsync(lease, page);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));

        Assert.Equal(EtlStoreStatus.Invalid, result.Status);
        Assert.Null(result.Lease);
        Assert.Equal(0, run.RetainedInputBytes);
        Assert.Empty(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("continuation-loop")]
    [InlineData("page-bound")]
    [InlineData("single-page")]
    [InlineData("continuation-bound")]
    public async Task Http_page_attachment_rejects_continuation_that_cannot_be_advanced(string defect)
    {
        var pipeline = EtlV2TestPipelines.Create();
        if (defect == "page-bound")
            pipeline = pipeline with { Http = pipeline.Http with { MaxPagesPerPartition = 1, MaxPageBytes = 64, MaxRetainedInputBytes = 128, MaxContinuationBytes = 16 } };
        else if (defect == "single-page")
            pipeline = pipeline with { Source = Assert.IsType<EtlHttpJsonSource>(pipeline.Source) with { Pagination = new EtlSinglePagePagination() } };
        else if (defect == "continuation-bound")
            pipeline = pipeline with { Http = pipeline.Http with { MaxContinuationBytes = 2 } };

        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission($"http-page-next-{defect}", new EtlScope("tenant-a", $"http-page-next-{defect}"), pipeline, ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        if (defect == "continuation-loop")
        {
            var firstPage = RetainedPage(lease, "page-0", byteLength: 10, recordCount: 0, nextContinuation: "cursor-a");
            var attached = await store.AttachTestHttpPageAsync(lease, firstPage);
            lease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);
            var advanced = await store.AdvanceEmptyHttpPageAsync(lease);
            lease = Assert.IsType<EtlConnectorPartitionLease>(advanced.Lease);
        }

        var nextContinuation = defect == "continuation-loop"
            ? Assert.IsType<EtlConnectorHttpPosition>(lease.Position).Request.Continuation
            : "cursor-long";
        var page = RetainedPage(lease, "invalid-next", byteLength: 10, recordCount: 0, nextContinuation);
        var result = await store.AttachTestHttpPageAsync(lease, page);

        Assert.Equal(EtlStoreStatus.Invalid, result.Status);
        Assert.Null(result.Lease);
        Assert.Equal(defect == "continuation-loop" ? 10 : 0, (await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId))!.RetainedInputBytes);
        Assert.Equal(defect == "continuation-loop" ? 1 : 0, await context.ConnectorHttpPages.CountAsync());
    }

    [Fact]
    public async Task Empty_http_pages_advance_without_accepting_records_and_success_waits_for_exhaustion()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var connector = (IEtlConnectorRuntimeStore)runtime;
        var admission = ConnectorAdmission("http-empty-pages", new EtlScope("tenant-a", "http-empty-pages"), httpShardIds: ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(connector, admission, "worker"));

        var earlySuccess = await runtime.SettleTerminalAsync(new(admission.Invocation.Scope, admission.Invocation.RunId, EtlTerminalOutcome.Succeeded, null));
        var firstPage = RetainedPage(lease, "empty-0", byteLength: 7, recordCount: 0, nextContinuation: "cursor-1");
        var first = await connector.AttachTestHttpPageAsync(lease, firstPage);
        var firstPageLease = Assert.IsType<EtlConnectorPartitionLease>(first.Lease);
        var afterFirst = await connector.AdvanceEmptyHttpPageAsync(firstPageLease);
        var advanceReplay = await connector.AdvanceEmptyHttpPageAsync(firstPageLease);
        var nextLease = Assert.IsType<EtlConnectorPartitionLease>(afterFirst.Lease);
        var nextPosition = Assert.IsType<EtlConnectorHttpPosition>(nextLease.Position);
        var stalePosition = await connector.AttachTestHttpPageAsync(firstPageLease, firstPage);
        var second = await connector.AttachTestHttpPageAsync(nextLease, RetainedPage(nextLease, "empty-1", byteLength: 11, recordCount: 0));
        var beforeExhaustion = await runtime.SettleTerminalAsync(new(admission.Invocation.Scope, admission.Invocation.RunId, EtlTerminalOutcome.Succeeded, null));
        var exhausted = await connector.AdvanceEmptyHttpPageAsync(Assert.IsType<EtlConnectorPartitionLease>(second.Lease));
        Assert.Single(await context.Terminals.AsNoTracking().ToListAsync());
        Assert.Single(await context.Outbox.AsNoTracking().ToListAsync());
        var success = await runtime.SettleTerminalAsync(new(admission.Invocation.Scope, admission.Invocation.RunId, EtlTerminalOutcome.Succeeded, null));
        var run = Assert.IsType<EtlConnectorRunView>(await connector.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));

        Assert.Equal(EtlStoreStatus.Conflict, earlySuccess.Status);
        Assert.Equal(EtlStoreStatus.Applied, first.Status);
        Assert.Equal(EtlStoreStatus.Applied, afterFirst.Status);
        Assert.Equal(EtlStoreStatus.Replay, advanceReplay.Status);
        Assert.Equal(nextLease.Position, advanceReplay.Lease!.Position);
        Assert.Equal(EtlStoreStatus.Conflict, stalePosition.Status);
        Assert.Equal(1, nextPosition.PageSequence);
        Assert.Equal("cursor-1", nextPosition.Request.Continuation);
        Assert.Null(nextPosition.Page);
        Assert.Equal(EtlStoreStatus.Applied, second.Status);
        Assert.Equal(EtlStoreStatus.Conflict, beforeExhaustion.Status);
        Assert.Equal(EtlStoreStatus.Applied, exhausted.Status);
        var finalPosition = Assert.IsType<EtlConnectorHttpPosition>(exhausted.Lease!.Position);
        Assert.Equal(1, finalPosition.PageSequence);
        Assert.Null(finalPosition.Page);
        Assert.True(finalPosition.Exhausted);
        Assert.Equal(EtlStoreStatus.Replay, success.Status);
        Assert.Equal(18, run.RetainedInputBytes);
        Assert.Equal(2, await context.ConnectorHttpPages.CountAsync());
        Assert.Empty(await context.Reservations.AsNoTracking().ToListAsync());
        Assert.Empty(await context.Manifest.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Advancing_nonempty_http_page_conflicts()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("http-page-nonempty", new EtlScope("tenant-a", "http-page-nonempty"), httpShardIds: ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var attached = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "records", byteLength: 9, recordCount: 1, nextContinuation: "cursor-1"));

        var nonempty = await store.AdvanceEmptyHttpPageAsync(attached.Lease!);
        var stale = await store.AdvanceEmptyHttpPageAsync(lease);

        Assert.Equal(EtlStoreStatus.Conflict, nonempty.Status);
        Assert.Equal(EtlStoreStatus.Conflict, stale.Status);
        Assert.Null(nonempty.Lease);
        Assert.Null(stale.Lease);
        Assert.Single(await context.ConnectorHttpPages.ToListAsync());
    }

    [Fact]
    public async Task Http_page_retention_budget_counts_prior_empty_pages()
    {
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Http = EtlV2TestPipelines.Create().Http with { MaxPageBytes = 10, MaxRetainedInputBytes = 15, MaxContinuationBytes = 8 }
        };
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("http-retention-budget", new EtlScope("tenant-a", "http-retention-budget"), pipeline, ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var first = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "budget-0", byteLength: 10, recordCount: 0, nextContinuation: "cursor-1"));
        var nextLease = Assert.IsType<EtlConnectorPartitionLease>((await store.AdvanceEmptyHttpPageAsync(first.Lease!)).Lease);

        var overBudget = await store.AttachTestHttpPageAsync(nextLease, RetainedPage(nextLease, "budget-1", byteLength: 10, recordCount: 0));
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));

        Assert.Equal(EtlStoreStatus.PermanentBudgetExceeded, overBudget.Status);
        Assert.Null(overBudget.Lease);
        Assert.Equal(10, run.RetainedInputBytes);
        Assert.Single(await context.ConnectorHttpPages.ToListAsync());
    }

    [Fact]
    public async Task Http_page_retention_aggregates_shards_and_remains_isolated_by_scope()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var scope = new EtlScope("tenant-a", "http-multi-shard");
        var admission = ConnectorAdmission("shared-run-id", scope, httpShardIds: ["shard-a", "shard-b"]);
        var leases = await StartHttpRunAsync(store, admission, "worker");
        Assert.Equal(2, leases.Count);
        foreach (var lease in leases)
        {
            var result = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, $"{lease.PartitionOrdinal}-page", byteLength: 13, recordCount: 1));
            Assert.Equal(EtlStoreStatus.Applied, result.Status);
        }

        var isolatedAdmission = ConnectorAdmission("shared-run-id", new EtlScope("tenant-a", "http-other-scope"), httpShardIds: ["shard-a"]);
        var isolatedLease = Assert.Single(await StartHttpRunAsync(store, isolatedAdmission, "other-worker"));
        Assert.Equal(EtlStoreStatus.Applied, (await store.AttachTestHttpPageAsync(isolatedLease, RetainedPage(isolatedLease, "isolated-page", byteLength: 5, recordCount: 0))).Status);
        var aggregate = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(scope, admission.Invocation.RunId));
        var isolated = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(isolatedAdmission.Invocation.Scope, isolatedAdmission.Invocation.RunId));

        Assert.Equal(26, aggregate.RetainedInputBytes);
        Assert.Equal(5, isolated.RetainedInputBytes);
        Assert.Equal(3, await context.ConnectorHttpPages.CountAsync());
    }

    [Fact]
    public async Task New_http_partition_lease_recovers_the_retained_page()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("http-page-recovery", new EtlScope("tenant-a", "http-page-recovery"), httpShardIds: ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "first-worker"));
        var page = RetainedPage(lease, "recoverable-page", byteLength: 17, recordCount: 3, nextContinuation: "cursor-1");
        var attached = await store.AttachTestHttpPageAsync(lease, page);

        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(attached.Lease!));
        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "replacement-worker"));
        var position = Assert.IsType<EtlConnectorHttpPosition>(recovered.Position);
        var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));

        Assert.Equal(lease.Fence + 1, recovered.Fence);
        Assert.Equal(page, position.Page);
        Assert.Equal(17, run.RetainedInputBytes);
    }

    [Fact]
    public async Task Http_page_attachment_rejects_stale_owner_and_expiry_during_save_rolls_back()
    {
        await using (var database = await TestDatabase.CreateAsync())
        await using (var context = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
            var admission = ConnectorAdmission("http-page-stale-owner", new EtlScope("tenant-a", "http-page-stale-owner"), httpShardIds: ["shard-a"]);
            var lease = Assert.Single(await StartHttpRunAsync(store, admission, "first-worker"));
            Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
            Assert.NotNull(await store.ClaimConnectorPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "replacement-worker"));

            var stale = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "stale-page", byteLength: 8, recordCount: 0));

            Assert.Equal(EtlStoreStatus.LeaseLost, stale.Status);
            Assert.Null(stale.Lease);
            Assert.Empty(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());
        }

        await using (var database = await TestDatabase.CreateAsync())
        {
            var clock = new MutableTimeProvider();
            var expire = new ConnectorExpireAfterSave(clock);
            await using var context = database.CreateContext(expire);
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
            var admission = ConnectorAdmission("http-page-expiry", new EtlScope("tenant-a", "http-page-expiry"), httpShardIds: ["shard-a"]);
            var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));

            expire.Armed = true;
            var expired = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "rolled-back-page", byteLength: 8, recordCount: 0));
            context.ChangeTracker.Clear();

            Assert.Equal(EtlStoreStatus.LeaseLost, expired.Status);
            Assert.Null(expired.Lease);
            Assert.Empty(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());
            Assert.Equal(EtlConnectorRecordCodec.Write(lease.Position), (await context.ConnectorPartitions.AsNoTracking().SingleAsync()).PositionJson);
            Assert.Equal(0, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedInputBytes);
        }
    }

    [Fact]
    public async Task Empty_http_page_advance_expiring_during_save_rolls_back()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var expire = new ConnectorExpireAfterSave(clock);
        await using var context = database.CreateContext(expire);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var admission = ConnectorAdmission("http-advance-expiry", new EtlScope("tenant-a", "http-advance-expiry"), httpShardIds: ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var attached = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "advance-expiry-page", byteLength: 12, recordCount: 0, nextContinuation: "cursor-1"));
        var attachedLease = Assert.IsType<EtlConnectorPartitionLease>(attached.Lease);

        expire.Armed = true;
        var expired = await store.AdvanceEmptyHttpPageAsync(attachedLease);
        context.ChangeTracker.Clear();

        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        Assert.Equal(EtlStoreStatus.LeaseLost, expired.Status);
        Assert.Null(expired.Lease);
        Assert.Equal(EtlConnectorRecordCodec.Write(attachedLease.Position), (await context.ConnectorPartitions.AsNoTracking().SingleAsync()).PositionJson);
        Assert.Single(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());
        Assert.Equal(12, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).RetainedInputBytes);
    }

    [Fact]
    public async Task Cancelled_connector_rejects_http_page_attachment_and_advance_as_terminal()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context);
        var store = (IEtlConnectorRuntimeStore)runtime;
        var admission = ConnectorAdmission("http-page-cancelled", new EtlScope("tenant-a", "http-page-cancelled"), httpShardIds: ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        Assert.Equal(EtlStoreStatus.Applied, (await runtime.CancelAsync(admission.Invocation)).Status);

        var attach = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "cancelled-page", byteLength: 8, recordCount: 0));
        var advance = await store.AdvanceEmptyHttpPageAsync(lease);

        Assert.Equal(EtlStoreStatus.Terminal, attach.Status);
        Assert.Equal(EtlStoreStatus.Terminal, advance.Status);
        Assert.Null(attach.Lease);
        Assert.Null(advance.Lease);
        Assert.Empty(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());
    }

    private static async Task<IReadOnlyList<EtlConnectorPartitionLease>> StartHttpRunAsync(
        IEtlConnectorRuntimeStore store, EtlConnectorRunAdmission admission, string workerId)
    {
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(admission)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(admission.Invocation.Scope, $"{workerId}-preparer"));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(preparation, new(null, null)));
        var leases = new List<EtlConnectorPartitionLease>(admission.Bindings.HttpShardIds.Count);
        for (var ordinal = 0; ordinal < admission.Bindings.HttpShardIds.Count; ordinal++)
        {
            leases.Add(Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
                admission.Invocation.Scope, admission.Invocation.RunId, workerId)));
        }
        return leases;
    }

    private static EtlRetainedHttpPage RetainedPage(EtlConnectorPartitionLease lease, string key,
        long byteLength, int recordCount, string? nextContinuation = null)
    {
        var position = Assert.IsType<EtlConnectorHttpPosition>(lease.Position);
        return new(position.PageSequence, EtlRuntimeIdentity.HttpRequestHash(position.Request),
            new EtlObjectReference(key, EtlRuntimeFixtures.Hash, byteLength), recordCount, nextContinuation);
    }
}
