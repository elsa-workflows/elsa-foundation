using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1196: a poll generation keeps a stalled attempt's late page away from later polls of the same cursor.</summary>
public sealed partial class EtlRuntimeStoreTests
{
    private const string CaughtUpBody = "{\"records\":[],\"next\":null}";
    private const string LatePageBody = "{\"records\":[{\"id\":1,\"name\":\"Ada\"}],\"next\":\"1\"}";
    private const string NextPollBody = "{\"records\":[{\"id\":1,\"name\":\"Ada\"},{\"id\":2,\"name\":\"Bram\"}],\"next\":\"2\"}";

    [Fact]
    public async Task A_stalled_attempts_late_page_never_reaches_a_later_poll_of_the_same_cursor()
    {
        var gained = false;
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, Volatile.Read(ref gained) ? NextPollBody : CaughtUpBody)));
        var clock = new MutableTimeProvider();
        await using var fixture = await HttpAcquisitionFixture.CreateAsync(this, server, clock: clock,
            pipeline: EtlV2TestPipelines.Create() with { Continuous = new EtlContinuousRunPolicy(1) });

        // A worker starts a poll and stalls past its lease.
        var stalled = Assert.Single(await StartHttpRunAsync(fixture.Store, fixture.Admission, "stalled"));
        Assert.Equal(0, stalled.PollGeneration);
        var stalledPermit = Assert.IsType<EtlHttpSourceFetchAttempt>(
            (await fixture.Store.BeginConnectorHttpSourceAsync(stalled, Guid.NewGuid().ToString("N"))).Permit);
        clock.Advance(TimeSpan.FromMinutes(10));

        // A successor polls the same cursor and catches up, clearing the journal that held the stalled attempt.
        await using var successorContext = fixture.Database.CreateContext();
        var successorStore = fixture.Database.CreateStore(successorContext, clock);
        var successor = Assert.IsType<EtlConnectorPartitionLease>(await successorStore.ClaimConnectorPartitionAsync(
            stalled.Scope, stalled.RunId, "successor"));
        for (var poll = 0; await CurrentJournalAsync(fixture) is { } journal; poll++)
        {
            Assert.True(poll < 5, "The successor never recorded its caught-up poll.");
            if (journal.NotBefore is { } notBefore && notBefore > clock.GetUtcNow()) clock.Advance(notBefore - clock.GetUtcNow());
            var polled = await fixture.AcquireAsync(successor, successorStore);
            Assert.Null(polled.FailureCode);
            Assert.Null(polled.Page);
        }
        Assert.Equal(1, (await fixture.Context.ConnectorPartitions.AsNoTracking().SingleAsync()).PollGeneration);

        // The stalled response arrives late with records the feed gained meanwhile, and lands under generation 0.
        var position = Assert.IsType<EtlConnectorHttpPosition>(stalled.Position);
        await using (var binding = await fixture.ResolveAsync())
        {
            var builder = new EtlHttpRequestBuilder((EtlHttpJsonSource)fixture.Pipeline.Source, binding, fixture.Pipeline.Http);
            var late = await new EtlHttpPageCache(fixture.Root, fixture.Admission, 0, builder, stalled.PollGeneration).PublishAsync(
                0, position.Request, Guid.ParseExact(stalledPermit.RequestId, "N"), stalled.Fence,
                stalledPermit.StartedAt.AddMilliseconds(1), Encoding.UTF8.GetBytes(LatePageBody));
            Assert.Equal(EtlStoreStatus.LeaseLost, (await fixture.Store.CaptureConnectorHttpSourceAsync(
                stalled, stalledPermit, late.Descriptor, late.Provenance.ResponseReceivedAtUtc)).Status);
        }
        Assert.Equal(EtlStoreStatus.Applied, await successorStore.ReleaseConnectorPartitionAsync(successor));

        // The next poll of that cursor runs in generation 1, so it fetches and attaches its own page.
        Volatile.Write(ref gained, true);
        clock.Advance(TimeSpan.FromSeconds(2));
        await using var nextContext = fixture.Database.CreateContext();
        var nextStore = fixture.Database.CreateStore(nextContext, clock);
        var next = Assert.IsType<EtlConnectorPartitionLease>(await nextStore.ClaimConnectorPartitionAsync(
            stalled.Scope, stalled.RunId, "next"));
        Assert.Equal(1, next.PollGeneration);
        var acquired = await fixture.AcquireAsync(next, nextStore);

        Assert.Null(acquired.FailureCode);
        var page = Assert.IsType<EtlCachedHttpPage>(acquired.Page);
        Assert.Equal(NextPollBody, Encoding.UTF8.GetString(page.RawBody.Span));
        Assert.Equal(next.Fence, page.Provenance.Fence);
        // Both generations' manifests exist side by side; neither overwrote the other.
        Assert.Equal(2, Directory.GetFiles(fixture.Root, "*.page").Length);
    }

    [Fact]
    public void Generation_zero_keeps_the_finite_page_identity_and_every_later_generation_is_distinct()
    {
        var admission = ConnectorAdmission("generation-identity", new("tenant", "generation"), httpShardIds: ["a"]);
        var request = new EtlHttpRequest("a", "cursor");
        var finite = EtlRuntimeIdentity.ConnectorHttpPageId(admission, 0, 3, request);

        Assert.Equal(finite, EtlRuntimeIdentity.ConnectorHttpPageId(admission, 0, 3, request, 0));
        var generations = Enumerable.Range(0, 4).Select(generation =>
            EtlRuntimeIdentity.ConnectorHttpPageId(admission, 0, 3, request, generation)).ToArray();
        Assert.Equal(generations.Length, generations.Distinct(StringComparer.Ordinal).Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => EtlRuntimeIdentity.ConnectorHttpPageId(admission, 0, 3, request, -1));
    }

    [Fact]
    public async Task A_finite_http_shard_keeps_its_whole_history_and_no_compacted_summary_across_claims()
    {
        await using var database = await EtlRuntimeTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("finite-history", new("tenant", "finite-history"), httpShardIds: ["shard"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        for (var sequence = 0; sequence < 3; sequence++)
        {
            var attached = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, $"page-{sequence}", 10, 0, $"cursor-{sequence}"));
            lease = (await store.AdvanceEmptyHttpPageAsync(attached.Lease!)).Lease!;
            Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
            lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
                admission.Invocation.Scope, admission.Invocation.RunId, "worker"));
        }

        context.ChangeTracker.Clear();
        Assert.Equal(3, await context.ConnectorHttpPages.CountAsync());
        Assert.Equal(4, await context.ConnectorHttpRequests.CountAsync());
        var partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync();
        Assert.Equal((0L, 0L, 3L), (partition.CompactedPageCount, partition.PollGeneration, partition.PageCount));
        Assert.Equal(0, lease.PollGeneration);
    }

    private static async Task<EtlConnectorHttpSourceFetch?> CurrentJournalAsync(HttpAcquisitionFixture fixture)
    {
        fixture.Context.ChangeTracker.Clear();
        var partition = await fixture.Context.ConnectorPartitions.AsNoTracking().SingleAsync();
        var request = await fixture.Context.ConnectorHttpRequests.AsNoTracking()
            .SingleAsync(row => row.PageSequence == partition.CurrentRequestSequence);
        return request.FetchJson is null ? null : EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(request.FetchJson);
    }
}
