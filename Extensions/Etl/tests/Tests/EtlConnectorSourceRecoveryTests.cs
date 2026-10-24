using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Source_recovery_closes_uncertain_attempt_and_captures_its_exact_permit_without_attaching_page()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var admission = ConnectorAdmission("source-recovery", new("tenant-a", "source-recovery"), httpShardIds: ["shard-a"]);
        EtlConnectorPartitionLease oldLease;
        EtlHttpSourceFetchAttempt originalPermit;
        DateTimeOffset initialDeadline;

        await using (var context = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
            oldLease = Assert.Single(await StartHttpRunAsync(store, admission, "original-worker"));
            var begin = await store.BeginConnectorHttpSourceAsync(oldLease, Guid.NewGuid().ToString("N"));

            Assert.Equal(EtlStoreStatus.Applied, begin.Status);
            originalPermit = Assert.IsType<EtlHttpSourceFetchAttempt>(begin.Permit);
            initialDeadline = Assert.IsType<EtlConnectorHttpSourceFetch>(begin.Fetch).Deadline;
            Assert.True(originalPermit.ExpiresAt <= initialDeadline);
            Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(oldLease));
        }

        await using (var context = database.CreateContext())
        {
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
            var replacement = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
                admission.Invocation.Scope, admission.Invocation.RunId, "replacement-worker"));
            Assert.NotEqual(oldLease.Fence, replacement.Fence);

            var page = RetainedPage(replacement, "source-recovery-page", 23, 1);
            var staleOwnerCapture = await store.CaptureConnectorHttpSourceAsync(oldLease, originalPermit, page, originalPermit.StartedAt);
            Assert.Equal(EtlStoreStatus.LeaseLost, staleOwnerCapture.Status);
            Assert.Empty(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());

            var uncertainClosure = await store.BeginConnectorHttpSourceAsync(replacement, Guid.NewGuid().ToString("N"));
            Assert.Equal(EtlStoreStatus.Applied, uncertainClosure.Status);
            Assert.Null(uncertainClosure.Permit);
            var uncertainFetch = Assert.IsType<EtlConnectorHttpSourceFetch>(uncertainClosure.Fetch);
            Assert.Equal(EtlConnectorHttpSourceFetchPhase.Waiting, uncertainFetch.Phase);
            Assert.Equal(initialDeadline, uncertainFetch.Deadline);
            var closedOriginal = Assert.Single(uncertainFetch.Attempts);
            Assert.Equal(originalPermit, closedOriginal.Attempt);
            Assert.Null(closedOriginal.Outcome);
            Assert.NotNull(closedOriginal.CompletedAt);

            var forgedPermit = originalPermit with { RequestId = Guid.NewGuid().ToString("N") };
            var forgedCapture = await store.CaptureConnectorHttpSourceAsync(replacement, forgedPermit, page, originalPermit.StartedAt);
            Assert.Equal(EtlStoreStatus.Conflict, forgedCapture.Status);
            Assert.Null(forgedCapture.Fetch!.Capture);

            clock.Advance(uncertainFetch.NotBefore!.Value - clock.GetUtcNow());
            var retry = await store.BeginConnectorHttpSourceAsync(replacement, Guid.NewGuid().ToString("N"));
            Assert.Equal(EtlStoreStatus.Applied, retry.Status);
            Assert.Equal(2, Assert.IsType<EtlHttpSourceFetchAttempt>(retry.Permit).Number);
            Assert.Equal(initialDeadline, retry.Fetch!.Deadline);

            var captured = await store.CaptureConnectorHttpSourceAsync(replacement, originalPermit, page, originalPermit.StartedAt);
            Assert.Equal(EtlStoreStatus.Applied, captured.Status);
            var capturedFetch = Assert.IsType<EtlConnectorHttpSourceFetch>(captured.Fetch);
            Assert.Equal(initialDeadline, capturedFetch.Deadline);
            Assert.Equal(originalPermit.RequestId, capturedFetch.Capture!.RequestId);
            Assert.Equal(originalPermit.Fence, capturedFetch.Capture.Fence);
            Assert.Equal(originalPermit.StartedAt, capturedFetch.Capture.ResponseReceivedAtUtc);
            Assert.Empty(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());
            var positionBeforeAttach = Assert.IsType<EtlConnectorHttpPosition>(replacement.Position);
            Assert.Null(positionBeforeAttach.Page);
            var sourceRequest = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync();
            Assert.Equal(EtlConnectorHttpSourceFetchPhase.Captured,
                EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(sourceRequest.FetchJson!).Phase);

            var attached = await store.AttachHttpPageAsync(replacement, page);
            Assert.Equal(EtlStoreStatus.Applied, attached.Status);
            Assert.Equal(page, Assert.IsType<EtlConnectorHttpPosition>(attached.Lease!.Position).Page);
            Assert.Single(await context.ConnectorHttpPages.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task Source_reservation_is_aggregate_and_capture_releases_unused_capacity_for_another_partition()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Http = defaults.Http with { MaxPageBytes = 64, MaxRetainedInputBytes = 100, MaxContinuationBytes = 16 }
        };
        var admission = ConnectorAdmission("source-aggregate-reservation", new("tenant-a", "source-aggregate-reservation"),
            pipeline, ["shard-a", "shard-b"]);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var leases = await StartHttpRunAsync(store, admission, "worker");
        var firstLease = leases[0];
        var secondLease = leases[1];

        var first = await store.BeginConnectorHttpSourceAsync(firstLease, Guid.NewGuid().ToString("N"));
        Assert.Equal(EtlStoreStatus.Applied, first.Status);
        Assert.NotNull(first.Permit);
        Assert.Equal(64, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).SourceInputReservedBytes);

        var blockedSecond = await store.BeginConnectorHttpSourceAsync(secondLease, Guid.NewGuid().ToString("N"));
        Assert.Equal(EtlStoreStatus.BudgetExceeded, blockedSecond.Status);
        Assert.Null(blockedSecond.Permit);
        Assert.Null(blockedSecond.Fetch);
        var requests = await context.ConnectorHttpRequests.AsNoTracking().OrderBy(row => row.PartitionOrdinal).ToArrayAsync();
        Assert.Equal(2, requests.Length);
        Assert.NotNull(requests[0].FetchJson);
        Assert.Null(requests[1].FetchJson);
        Assert.Equal(0, requests[1].InputReservationBytes);
        Assert.Equal(0, requests[1].FetchMetadataBytes);
        Assert.Equal(0, requests[1].SourceMetadataReserveBytes);

        var smallPage = RetainedPage(firstLease, "source-small-capture", 36, 1);
        var capture = await store.CaptureConnectorHttpSourceAsync(firstLease, first.Permit!, smallPage, first.Permit!.StartedAt);
        Assert.Equal(EtlStoreStatus.Applied, capture.Status);
        Assert.Equal(36, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).SourceInputReservedBytes);

        var availableSecond = await store.BeginConnectorHttpSourceAsync(secondLease, Guid.NewGuid().ToString("N"));
        Assert.Equal(EtlStoreStatus.Applied, availableSecond.Status);
        Assert.NotNull(availableSecond.Permit);
        Assert.Equal(100, (await context.ConnectorRuns.AsNoTracking().SingleAsync()).SourceInputReservedBytes);
        requests = await context.ConnectorHttpRequests.AsNoTracking().OrderBy(row => row.PartitionOrdinal).ToArrayAsync();
        Assert.All(requests, request => Assert.NotNull(request.FetchJson));
    }

    [Fact]
    public async Task Source_begin_lease_expiry_during_save_rolls_back_journal_and_capacity_reservation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        var expire = new ConnectorExpireAfterSave(clock);
        await using var context = database.CreateContext(expire);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        var defaults = EtlV2TestPipelines.Create();
        // The permit outlives the partition lease so only the ownership check can reject this save.
        var pipeline = defaults with { Http = defaults.Http with { RequestTimeoutSeconds = 60 } };
        var admission = ConnectorAdmission("source-begin-expiry", new("tenant-a", "source-begin-expiry"), pipeline, ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));

        expire.Armed = true;
        var expired = await store.BeginConnectorHttpSourceAsync(lease, Guid.NewGuid().ToString("N"));
        context.ChangeTracker.Clear();

        Assert.Equal(EtlStoreStatus.LeaseLost, expired.Status);
        Assert.Null(expired.Fetch);
        Assert.Null(expired.Permit);
        var request = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync();
        Assert.Null(request.FetchJson);
        Assert.Null(request.FetchDigest);
        Assert.Equal(0, request.FetchMetadataBytes);
        Assert.Equal(0, request.SourceMetadataReserveBytes);
        Assert.Equal(0, request.InputReservationBytes);
        var connector = await context.ConnectorRuns.AsNoTracking().SingleAsync();
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();
        Assert.Equal(0, connector.SourceInputReservedBytes);
        Assert.Equal(0, partition.SourceInputReservedBytes);
    }
}
