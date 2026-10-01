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
    /// A schedule deleted and saved again under the same id, as artifact-scoped indexing replaces it, fences out the claim
    /// on its predecessor: its claimant can neither settle nor renew it, and the recreated schedule is claimable at once.
    /// The recreated schedule restarts its revision, yet its claim does not reissue the predecessor's fencing token, even to
    /// the same owner (#2198). Deleting the schedule fences out the claim on it as well.
    /// </summary>
    public static async Task DeletingAndSavingTheScheduleAgainFencesOutItsClaimWithoutReissuingTheTokenAsync(Func<IRecurringTriggerScheduleStore> node)
    {
        var store = node();
        var indexer = node();
        await store.SaveAsync(Schedule);
        var claim = Assert.Single(await store.ClaimDueAsync(Request("pump", Now)));

        await indexer.DeleteByArtifactAsync(Schedule.ArtifactId);
        await indexer.SaveAsync(Schedule with { CreatedAt = Now });

        Assert.False(await store.SettleClaimAsync(claim, Now.AddMinutes(1)));
        Assert.Equal(Due, (await store.FindAsync(Schedule.ScheduleId))!.NextOccurrence);
        var reclaimed = Assert.Single(await store.ClaimDueAsync(Request("pump", Now)));
        Assert.True(reclaimed.FencingToken > claim.FencingToken);
        Assert.Null(await store.RenewClaimAsync(claim, Now, Lease));

        await indexer.DeleteAsync(Schedule.ScheduleId);

        Assert.Null(await store.RenewClaimAsync(reclaimed, Now, Lease));
        Assert.False(await store.ReleaseClaimAsync(reclaimed, Now));
    }

    /// <summary>
    /// Two stores both read one due row before either writes its claim. Exactly one claim is granted: the loser's write is
    /// refused by the row revision it read, which on PostgreSQL's read committed is the only thing that stops it.
    /// </summary>
    public static async Task TwoStoresThatReadOneDueRowGrantExactlyOneClaimAsync(EfRecurringScheduleStores stores)
    {
        await stores.Create().SaveAsync(Schedule);
        var rendezvous = new ClaimWriteRendezvous(participants: 2);

        var claims = await Task.WhenAll(
            stores.Create(rendezvous.Participant()).ClaimDueAsync(Request("first", Now)).AsTask(),
            stores.Create(rendezvous.Participant()).ClaimDueAsync(Request("second", Now)).AsTask());

        var granted = Assert.Single(claims.SelectMany(batch => batch));
        Assert.True(await stores.Create().SettleClaimAsync(granted, Now.AddMinutes(1)));
    }

    /// <summary>
    /// Activating a replacement publication of the slot hands the replaced schedule's due, unsettled occurrence to the
    /// replacement's schedule of the same trigger, in the same write that deactivates the replaced schedule (#2198). The
    /// claim a pump holds on the replaced schedule is stale from then on, the replacement's schedule is claimable at once on
    /// that occurrence, and activating the replacement again takes nothing over a second time.
    /// </summary>
    public static async Task ActivatingAReplacementTakesOverTheDueOccurrenceAndFencesOutTheReplacedClaimAsync(Func<IRecurringTriggerScheduleStore> node)
    {
        var store = node();
        await ActivateAsync(store, Slotted("publication-a", "artifact-a", next: Due, createdAt: Due.AddHours(-1)), replacedActivationId: null);
        var claim = Assert.Single(await store.ClaimDueAsync(Request("pump", Now)));

        // Materialized after the occurrence fell due, so its own cursor is the first occurrence after its creation.
        var replacement = Slotted("publication-b", "artifact-b", next: Now.AddSeconds(50), createdAt: Now.AddSeconds(-10));
        await ActivateAsync(node(), replacement, replacedActivationId: "publication-a");

        Assert.Null(await store.RenewClaimAsync(claim, Now, Lease));
        Assert.False(await store.SettleClaimAsync(claim, Now.AddMinutes(1)));
        var takenOver = Assert.Single(await store.ClaimDueAsync(Request("pump", Now)));
        Assert.Equal(replacement.ScheduleId, takenOver.Schedule.ScheduleId);
        Assert.Equal(Due, takenOver.Schedule.NextOccurrence);

        Assert.True(await store.SettleClaimAsync(takenOver, Now.AddMinutes(1)));
        await node().ActivateAsync("publication-b", "publication-a");
        Assert.Equal(Now.AddMinutes(1), (await store.FindAsync(replacement.ScheduleId))!.NextOccurrence);
    }

    /// <summary>
    /// The replacement keeps its own cursor when there is nothing to take over: the replaced occurrence fell due only after
    /// the replacement was materialized (its own cursor covers that), or the replaced schedule is another trigger.
    /// </summary>
    public static async Task ActivatingAReplacementKeepsItsOwnCursorWhenNoDueOccurrenceOfItsTriggerPrecededItAsync(Func<IRecurringTriggerScheduleStore> node)
    {
        var store = node();
        await store.PrepareActivationAsync("publication-a",
        [
            Slotted("publication-a", "artifact-a", next: Now.AddSeconds(30), createdAt: Due.AddHours(-1)),
            Slotted("publication-a", "artifact-a", next: Due, createdAt: Due.AddHours(-1), node: "node-removed")
        ]);
        await store.ActivateAsync("publication-a", replacedActivationId: null);

        var replacement = Slotted("publication-b", "artifact-b", next: Now.AddSeconds(50), createdAt: Now.AddSeconds(-10));
        await ActivateAsync(store, replacement, replacedActivationId: "publication-a");

        Assert.Equal(replacement.NextOccurrence, (await store.FindAsync(replacement.ScheduleId))!.NextOccurrence);
        Assert.Empty(await store.ClaimDueAsync(Request("pump", Now)));
    }

    private static RecurringTriggerOccurrenceClaimRequest Request(string owner, DateTimeOffset now) => new(owner, now, Lease, 10);

    // A trigger's schedule in one publication of the slot "slot-claims", as activation preparation materializes it.
    private static RecurringTriggerSchedule Slotted(string activationId, string artifactId, DateTimeOffset next, DateTimeOffset createdAt, string node = "node-claims") =>
        new(RecurringTriggerSchedule.BuildId(activationId, artifactId, node), artifactId, node, "Timer", "hash-claims",
            RecurringScheduleKind.Interval, "PT1M", next, createdAt, activationId, "slot-claims");

    private static async Task ActivateAsync(IRecurringTriggerScheduleStore store, RecurringTriggerSchedule schedule, string? replacedActivationId)
    {
        await store.PrepareActivationAsync(schedule.ActivationId!, [schedule]);
        await store.ActivateAsync(schedule.ActivationId!, replacedActivationId);
    }
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
