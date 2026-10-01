using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The occurrence-claim transitions of <see cref="IRecurringTriggerScheduleStore"/> (#2198), written once so the in-memory
/// store, SQLite and each native provider are held to the same outcome. <c>node</c> hands out one view of the same storage
/// per call, the way two nodes see one database. The claim is the occurrence's in-flight marker: it never moves the cursor,
/// only settling does, and a claim stops being current the moment anything else changes the schedule.
/// </summary>
internal static class RecurringOccurrenceClaimContract
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = Now.AddSeconds(-30);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);
    private static readonly RecurringTriggerSchedule Schedule = new(
        RecurringTriggerSchedule.BuildId("artifact-claims", "node-claims"), "artifact-claims", "node-claims", "Timer", "hash-claims",
        RecurringScheduleKind.Interval, "PT1M", Due, Due.AddHours(-1));

    /// <summary>
    /// A claim leaves the occurrence in the cursor and hides it from every other claimant until its lease lapses. The peer
    /// that then claims it fences the first claimant out of every transition, and only the peer's settlement moves the cursor.
    /// </summary>
    public static async Task AClaimHoldsTheOccurrenceUntilItsLeaseLapsesAsync(Func<IRecurringTriggerScheduleStore> node)
    {
        var first = node();
        var peer = node();
        await first.SaveAsync(Schedule);

        var claim = Assert.Single(await first.ClaimDueAsync(Request("first", Now)));
        Assert.Equal(Schedule, claim.Schedule);
        Assert.Equal(Due, (await peer.FindAsync(Schedule.ScheduleId))!.NextOccurrence);
        Assert.Empty(await peer.ClaimDueAsync(Request("peer", Now + Lease / 2)));

        var takeover = Assert.Single(await peer.ClaimDueAsync(Request("peer", Now + Lease)));
        Assert.True(takeover.FencingToken > claim.FencingToken);
        Assert.Null(await first.RenewClaimAsync(claim, Now + Lease, Lease));
        Assert.False(await first.SettleClaimAsync(claim, Now.AddMinutes(1)));
        Assert.False(await first.ReleaseClaimAsync(claim, Now.AddMinutes(1)));
        Assert.Equal(Due, (await peer.FindAsync(Schedule.ScheduleId))!.NextOccurrence);

        Assert.True(await peer.SettleClaimAsync(takeover, Now.AddMinutes(2)));
        Assert.Equal(Now.AddMinutes(2), (await first.FindAsync(Schedule.ScheduleId))!.NextOccurrence);
        Assert.Empty(await first.ClaimDueAsync(Request("first", Now.AddMinutes(1))));
        Assert.Equal(Now.AddMinutes(2), Assert.Single(await first.ClaimDueAsync(Request("first", Now.AddMinutes(2)))).Schedule.NextOccurrence);
    }

    /// <summary>
    /// A release after a failed fire keeps the occurrence in the cursor, hides it until the backoff, and counts the failure.
    /// A renewal supersedes the claim it renewed, and a settlement starts the next occurrence's count afresh.
    /// </summary>
    public static async Task AReleaseKeepsTheOccurrenceAndCountsTheFailureAsync(Func<IRecurringTriggerScheduleStore> node)
    {
        var store = node();
        await store.SaveAsync(Schedule);
        var failed = Assert.Single(await store.ClaimDueAsync(Request("pump", Now)));

        Assert.True(await store.ReleaseClaimAsync(failed, Now.AddSeconds(10)));
        Assert.Equal(Due, (await store.FindAsync(Schedule.ScheduleId))!.NextOccurrence);
        Assert.Empty(await store.ClaimDueAsync(Request("pump", Now.AddSeconds(5))));

        var retry = Assert.Single(await store.ClaimDueAsync(Request("pump", Now.AddSeconds(10))));
        Assert.Equal(1, retry.FailureCount);
        var renewed = await store.RenewClaimAsync(retry, Now.AddSeconds(20), Lease);
        Assert.NotNull(renewed);
        Assert.Equal(Now.AddSeconds(20) + Lease, renewed.VisibleAfter);
        Assert.False(await store.SettleClaimAsync(retry, Now.AddMinutes(1)));
        Assert.True(await store.SettleClaimAsync(renewed, Now.AddMinutes(1)));

        Assert.Equal(0, Assert.Single(await store.ClaimDueAsync(Request("pump", Now.AddMinutes(1)))).FailureCount);
    }

    /// <summary>
    /// A republish rewrites the schedule, and a delete removes it: either way the claim on it is no longer current, so its
    /// claimant can neither settle nor renew it, and the rewritten schedule is claimable at once.
    /// </summary>
    public static async Task RewritingOrDeletingTheScheduleFencesOutItsClaimAsync(Func<IRecurringTriggerScheduleStore> node)
    {
        var store = node();
        var republisher = node();
        await store.SaveAsync(Schedule);
        var claim = Assert.Single(await store.ClaimDueAsync(Request("pump", Now)));

        await republisher.DeleteByArtifactAsync(Schedule.ArtifactId);
        await republisher.SaveAsync(Schedule with { CreatedAt = Now });

        Assert.False(await store.SettleClaimAsync(claim, Now.AddMinutes(1)));
        Assert.Equal(Due, (await store.FindAsync(Schedule.ScheduleId))!.NextOccurrence);
        var reclaimed = Assert.Single(await store.ClaimDueAsync(Request("pump", Now)));

        await republisher.DeleteAsync(Schedule.ScheduleId);

        Assert.Null(await store.RenewClaimAsync(reclaimed, Now, Lease));
        Assert.False(await store.ReleaseClaimAsync(reclaimed, Now));
    }

    private static RecurringTriggerOccurrenceClaimRequest Request(string owner, DateTimeOffset now) => new(owner, now, Lease, 10);
}

/// <summary>
/// Hands out EF recurring-schedule stores over one database, each on a context of its own, as separate nodes have, and
/// disposes those contexts.
/// </summary>
internal sealed class EfRecurringScheduleStores(Func<IInterceptor[], RuntimeDbContext> createContext) : IAsyncDisposable
{
    private const string Scope = "recurring-occurrences";
    private readonly List<RuntimeDbContext> _contexts = [];

    /// <summary>Creates the schema, for a database no runtime node has migrated.</summary>
    public async Task<EfRecurringScheduleStores> EnsureCreatedAsync()
    {
        await using var context = createContext([]);
        await context.Database.EnsureCreatedAsync();
        return this;
    }

    public EfRecurringTriggerScheduleStore Create(params IInterceptor[] interceptors)
    {
        var context = createContext(interceptors);
        _contexts.Add(context);
        return new(context, new ScopeAccessor(), new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) })));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts)
            await context.DisposeAsync();
    }

    private sealed class ScopeAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(Scope));
    }
}
