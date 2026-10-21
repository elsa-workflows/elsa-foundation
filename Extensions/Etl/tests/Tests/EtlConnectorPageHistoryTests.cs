using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Http_page_history_prevents_return_to_an_earlier_request()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("request-history", new("tenant", "history"), httpShardIds: ["shard"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        foreach (var continuation in new[] { "a", "b" })
        {
            var attached = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, $"page-{continuation}", 10, 0, continuation));
            Assert.Equal(EtlStoreStatus.Applied, attached.Status);
            var advanced = await store.AdvanceEmptyHttpPageAsync(attached.Lease!);
            Assert.Equal(EtlStoreStatus.Applied, advanced.Status);
            lease = advanced.Lease!;
        }
        var result = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "page-return", 10, 0, "a"));
        Assert.Equal(EtlStoreStatus.Invalid, result.Status);
        Assert.Equal(2, await context.ConnectorHttpPages.CountAsync());
        Assert.Equal(20, (await store.FindConnectorRunAsync(admission.Invocation.Scope, admission.Invocation.RunId))!.RetainedInputBytes);
    }

    [Fact]
    public async Task Corrupt_older_page_json_cannot_remove_request_membership_and_fresh_claim_refuses_run()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("altered-chain", new("tenant", "history"), httpShardIds: ["shard"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        foreach (var continuation in new[] { "a", "b", "c", "d" })
        {
            var attached = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, $"page-{continuation}", 10, 0, continuation));
            Assert.Equal(EtlStoreStatus.Applied, attached.Status);
            lease = (await store.AdvanceEmptyHttpPageAsync(attached.Lease!)).Lease!;
        }
        var prior = await context.ConnectorHttpPages.SingleAsync(page => page.PageSequence == 1);
        var originalRequestHash = prior.RequestHash;
        prior.PageJson = "{}";
        await context.SaveChangesAsync();

        Assert.Contains(originalRequestHash,
            await context.ConnectorHttpRequests.AsNoTracking().Select(request => request.RequestHash).ToListAsync());
        Assert.Equal(EtlStoreStatus.Invalid,
            (await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "revisited", 10, 0, "a"))).Status);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));

        await using var recoveryContext = database.CreateContext();
        var recoveryStore = database.CreateStore(recoveryContext);
        await Assert.ThrowsAsync<InvalidDataException>(() => recoveryStore.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "replacement").AsTask());
        Assert.Equal(4, await recoveryContext.ConnectorHttpPages.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_maximum_continuations_fit_the_retained_position_and_recover(bool escapedBase64)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var pipeline = EtlV2TestPipelines.Create();
        pipeline = pipeline with { Http = pipeline.Http with { MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes } };
        var admission = ConnectorAdmission("maximum-cursors", new("tenant", "history"), pipeline, ["shard"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var first = escapedBase64
            ? string.Concat(Enumerable.Repeat("a>a", 21_845)) + "a"
            : new string('a', EtlHttpPolicy.MaximumSupportedContinuationBytes);
        var second = escapedBase64
            ? string.Concat(Enumerable.Repeat("b>b", 21_845)) + "b"
            : new string('b', EtlHttpPolicy.MaximumSupportedContinuationBytes);
        var attached = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "first", 10, 0, first));
        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        lease = (await store.AdvanceEmptyHttpPageAsync(attached.Lease!)).Lease!;
        attached = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "second", 10, 0, second));
        Assert.Equal(EtlStoreStatus.Applied, attached.Status);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(attached.Lease!));
        var recovered = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "replacement"));
        var position = Assert.IsType<EtlConnectorHttpPosition>(recovered.Position);
        Assert.Equal(first, position.Request.Continuation);
        Assert.Equal(second, position.Page!.NextContinuation);
        if (escapedBase64)
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(position)) > 512 * 1024);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("page-json")]
    [InlineData("byte-length")]
    [InlineData("request-hash")]
    [InlineData("false-exhaustion")]
    public async Task Corrupt_http_page_history_cannot_authorize_progress(string defect)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("history-corruption", new("tenant", "history"), httpShardIds: ["shard"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var page = RetainedPage(lease, "page", 10, 1);
        var result = await store.AttachTestHttpPageAsync(lease, page);
        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        var history = await context.ConnectorHttpPages.SingleAsync();
        switch (defect)
        {
            case "missing": context.ConnectorHttpPages.Remove(history); break;
            case "page-json": history.PageJson = "{}"; break;
            case "byte-length": history.ByteLength++; break;
            case "request-hash": history.RequestHash = new string('c', 64); break;
            case "false-exhaustion":
                var partition = await context.ConnectorPartitions.SingleAsync();
                partition.PositionJson = EtlConnectorRecordCodec.Write<EtlConnectorPosition>(
                    (EtlConnectorHttpPosition)result.Lease!.Position with { Page = null, Exhausted = true });
                partition.Completed = true;
                break;
        }
        if (defect == "request-hash")
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Empty(await context.Terminals.AsNoTracking().ToListAsync());
            return;
        }

        await context.SaveChangesAsync();
        if (defect == "false-exhaustion")
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SettleTerminalAsync(
                new(admission.Invocation.Scope, admission.Invocation.RunId, EtlTerminalOutcome.Succeeded, null)).AsTask());
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => store.RenewConnectorPartitionAsync(result.Lease!).AsTask());
        Assert.Empty(await context.Terminals.AsNoTracking().ToListAsync());
    }
}
