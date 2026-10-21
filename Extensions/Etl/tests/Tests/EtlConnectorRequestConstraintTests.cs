using Elsa.Etl.Core.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Theory]
    [InlineData("delete-page")]
    [InlineData("change-next-request")]
    [InlineData("delete-request")]
    [InlineData("null-predecessor")]
    public async Task Database_request_chain_constraints_reject_incomplete_history(string defect)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("request-constraints", new("tenant", "request-constraints"), httpShardIds: ["shard"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        for (var sequence = 0; sequence < 2; sequence++)
        {
            var attached = await store.AttachHttpPageAsync(lease, RetainedPage(lease, $"page-{sequence}", 10, 0, $"cursor-{sequence}"));
            Assert.Equal(EtlStoreStatus.Applied, attached.Status);
            var advanced = await store.AdvanceEmptyHttpPageAsync(attached.Lease!);
            Assert.Equal(EtlStoreStatus.Applied, advanced.Status);
            lease = advanced.Lease!;
        }
        // Run the actual database constraints, independently of EF's immutable-key checks.
        var exception = await Assert.ThrowsAsync<SqliteException>(() => defect switch
        {
            "delete-page" => context.Database.ExecuteSqlRawAsync("DELETE FROM elsa_etl_connector_http_pages WHERE PageSequence = 0"),
            "change-next-request" => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_http_pages SET NextRequestHash = {new string('d', 64)} WHERE PageSequence = 0"),
            "delete-request" => context.Database.ExecuteSqlRawAsync("DELETE FROM elsa_etl_connector_http_requests WHERE PageSequence = 0"),
            "null-predecessor" => context.Database.ExecuteSqlRawAsync("UPDATE elsa_etl_connector_http_requests SET PreviousPageSequence = NULL, PreviousHasContinuation = NULL WHERE PageSequence = 1"),
            _ => throw new InvalidOperationException()
        });
        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(2, await context.ConnectorHttpPages.CountAsync());
        Assert.Equal(3, await context.ConnectorHttpRequests.CountAsync());
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(lease));
        await using var recoveryContext = database.CreateContext();
        Assert.NotNull(await database.CreateStore(recoveryContext).ClaimConnectorPartitionAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, "replacement"));
    }
}
