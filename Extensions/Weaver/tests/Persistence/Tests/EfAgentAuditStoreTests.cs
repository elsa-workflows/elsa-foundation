using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>#3441 T028, research R10: audit records and their owner association survive a restart and scope owner reads.</summary>
public sealed class EfAgentAuditStoreTests
{
    [Fact]
    public async Task A_new_store_reads_the_owners_records_newest_first_scoped_before_the_limit()
    {
        using var database = new AgentSqliteDatabase();
        var audit = database.AuditStore();
        var at = DateTimeOffset.UtcNow;
        Assert.True(await audit.AssociateAsync("session-1", "actor-1", "tenant-1"));
        Assert.True(await audit.AssociateAsync("session-2", "actor-2", "tenant-1"));
        await audit.EmitAsync(Event("first", "session-1", at));
        await audit.EmitAsync(Event("tie-earlier", "session-1", at.AddSeconds(1)));
        await audit.EmitAsync(Event("tie-later", "session-1", at.AddSeconds(1)));
        await audit.EmitAsync(Event("foreign", "session-2", at.AddSeconds(5)));
        await audit.EmitAsync(Event("unassociated", "session-3", at.AddSeconds(6)));
        await audit.EmitAsync(Event("sessionless", null, at.AddSeconds(7)));

        // A restart: a new store instance over the same database.
        var restarted = database.AuditStore();
        var owned = await restarted.ListOwnedAsync(new("actor-1", "tenant-1", null, 10));
        Assert.True(owned.Succeeded);
        Assert.Equal(["tie-later", "tie-earlier", "first"], owned.Events.Select(x => x.Id));
        Assert.Equal(["tie-later"], (await restarted.ListOwnedAsync(new("actor-1", "tenant-1", null, 1))).Events.Select(x => x.Id));
        Assert.Equal(["tie-later", "tie-earlier", "first"], (await restarted.ListOwnedAsync(new("actor-1", "tenant-1", "session-1", 10))).Events.Select(x => x.Id));
        Assert.Empty((await restarted.ListOwnedAsync(new("actor-1", "tenant-2", null, 10))).Events);
        Assert.Empty((await restarted.ListOwnedAsync(new("actor-1", "tenant-1", "session-2", 10))).Events);
        Assert.Equal(Event("first", "session-1", at).Metadata, owned.Events.Last().Metadata);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => restarted.ListOwnedAsync(new("actor-1", "tenant-1", null, AgentAuditOwnerQuery.MaxTake + 1)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => restarted.ListOwnedAsync(new(" ", "tenant-1", null, 1)));

        // The trusted read is unscoped, newest first, and keeps the append order of records created at the same time.
        Assert.Equal(["sessionless", "unassociated", "foreign", "tie-earlier", "tie-later", "first"], (await restarted.ListAsync()).Select(x => x.Id));
        Assert.Equal(["tie-earlier", "tie-later"], (await restarted.ListAsync("session-1", 2)).Select(x => x.Id));
    }

    [Fact]
    public async Task The_first_owner_association_wins_across_restarts()
    {
        using var database = new AgentSqliteDatabase();
        Assert.True(await database.AuditStore().AssociateAsync("session-1", "actor-1", "tenant-1"));

        var restarted = database.AuditStore();
        Assert.True(await restarted.AssociateAsync("session-1", "actor-1", "tenant-1"));
        Assert.False(await restarted.AssociateAsync("session-1", "actor-2", "tenant-1"));
        Assert.False(await restarted.AssociateAsync("session-1", "actor-1", "tenant-2"));
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => database.AuditStore().AssociateAsync("session-2", $"actor-{index % 2}", "tenant-1")));
        Assert.True(concurrent.Count(x => x) is 3, "Exactly the associations agreeing with the first one succeed.");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => restarted.AssociateAsync(" ", "actor-1", "tenant-1"));
    }

    /// <summary>
    /// Many sessions recovering at once append their audits together. Every append from a separate connection succeeds,
    /// and the append order stays distinct and gap-free.
    /// </summary>
    [Fact]
    public async Task A_burst_of_concurrent_appends_all_succeed_in_a_distinct_gap_free_order()
    {
        using var database = new AgentSqliteDatabase();
        var pools = Enumerable.Range(0, 24).Select(_ => database.NewContexts()).ToArray();
        try
        {
            var at = DateTimeOffset.UtcNow;
            await Task.WhenAll(pools.Select((pool, index) => database.AuditStore(contexts: pool).EmitAsync(Event($"burst-{index}", $"session-{index}", at))));
        }
        finally
        {
            foreach (var pool in pools) await pool.DisposeAsync();
        }

        await using var db = database.Open();
        var sequences = await db.Set<AgentAuditRow>().OrderBy(x => x.Sequence).Select(x => x.Sequence).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 24).Select(x => (long)x), sequences);
        Assert.Equal(24, (await database.AuditStore().ListAsync()).Select(x => x.Id).Distinct().Count());
    }

    private static AgentAuditEvent Event(string id, string? sessionId, DateTimeOffset at) => new(id, AgentAuditEventKind.MessageAccepted, sessionId, "actor-1",
        "Summary of " + id, at, new Dictionary<string, string> { ["id"] = id, ["settledBy"] = "user" });
}
