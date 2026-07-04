using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Contract coverage for the durable <see cref="IRecurringTriggerScheduleStore"/> Groundwork bridge (W16). The
/// same assertions run against both a real Groundwork SQLite provider and an in-memory document store, proving
/// the recurring-start bridge is provider-neutral like its durable-timer sibling.
/// </summary>
public sealed class GroundworkRecurringTriggerScheduleStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_Find_RoundTripsSchedule(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        var saved = await store.SaveAsync(NewSchedule("artifact-1", "node-1", nextOffset: TimeSpan.FromMinutes(5)));
        Assert.Equal(RecurringTriggerSchedule.BuildId("artifact-1", "node-1"), saved.ScheduleId);

        var found = await store.FindAsync(saved.ScheduleId);
        Assert.NotNull(found);
        Assert.Equal("Timer", found!.StimulusType);
        Assert.Equal(RecurringScheduleKind.Interval, found.Kind);
        Assert.Equal("PT5M", found.Expression);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), found.NextOccurrence);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListDue_ReturnsOnlyDue_OrderedByNextThenId_CappedByLimit(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        await store.SaveAsync(NewSchedule("art", "b", nextOffset: TimeSpan.FromMinutes(-2)));
        await store.SaveAsync(NewSchedule("art", "a", nextOffset: TimeSpan.FromMinutes(-2)));
        await store.SaveAsync(NewSchedule("art", "early", nextOffset: TimeSpan.FromMinutes(-5)));
        await store.SaveAsync(NewSchedule("art", "future", nextOffset: TimeSpan.FromMinutes(10)));

        var due = await store.ListDueAsync(Now, limit: 10);
        Assert.Equal(
            new[]
            {
                RecurringTriggerSchedule.BuildId("art", "early"),
                RecurringTriggerSchedule.BuildId("art", "a"),
                RecurringTriggerSchedule.BuildId("art", "b")
            },
            due.Select(s => s.ScheduleId));

        var limited = await store.ListDueAsync(Now, limit: 2);
        Assert.Equal(
            new[]
            {
                RecurringTriggerSchedule.BuildId("art", "early"),
                RecurringTriggerSchedule.BuildId("art", "a")
            },
            limited.Select(s => s.ScheduleId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_IsUpsert_RepublishReplaces(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        await store.SaveAsync(NewSchedule("art", "node", nextOffset: TimeSpan.FromMinutes(5), expression: "PT5M"));
        await store.SaveAsync(NewSchedule("art", "node", nextOffset: TimeSpan.FromMinutes(9), expression: "PT9M"));

        // Unlike the durable-timer existing-wins rule, a recurring schedule is rewritten on republish.
        var found = await store.FindAsync(RecurringTriggerSchedule.BuildId("art", "node"));
        Assert.Equal("PT9M", found!.Expression);
        Assert.Equal(Now + TimeSpan.FromMinutes(9), found.NextOccurrence);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task TryAdvance_ClaimsOccurrence_OnlyWhenCursorMatches(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        var schedule = await store.SaveAsync(NewSchedule("art", "node", nextOffset: TimeSpan.FromMinutes(-1)));
        var expected = schedule.NextOccurrence;
        var advanced = expected + TimeSpan.FromMinutes(5);

        // Stale expectation loses the CAS.
        Assert.False(await store.TryAdvanceAsync(schedule.ScheduleId, expected + TimeSpan.FromSeconds(1), advanced));

        // Correct expectation wins and moves the cursor.
        Assert.True(await store.TryAdvanceAsync(schedule.ScheduleId, expected, advanced));
        Assert.Equal(advanced, (await store.FindAsync(schedule.ScheduleId))!.NextOccurrence);

        // Re-claiming the already-advanced occurrence loses (at-most-once).
        Assert.False(await store.TryAdvanceAsync(schedule.ScheduleId, expected, advanced));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task DeleteByArtifact_RemovesOnlyThatArtifactsSchedules(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        await store.SaveAsync(NewSchedule("art-1", "a", nextOffset: TimeSpan.FromMinutes(-1)));
        await store.SaveAsync(NewSchedule("art-1", "b", nextOffset: TimeSpan.FromMinutes(-1)));
        await store.SaveAsync(NewSchedule("art-2", "c", nextOffset: TimeSpan.FromMinutes(-1)));

        await store.DeleteByArtifactAsync("art-1");

        var remaining = await store.ListDueAsync(Now, limit: 10);
        Assert.Equal(new[] { RecurringTriggerSchedule.BuildId("art-2", "c") }, remaining.Select(s => s.ScheduleId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Delete_RemovesSchedule_AndDeletingMissingIsNoOp(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        var schedule = await store.SaveAsync(NewSchedule("art", "node", nextOffset: TimeSpan.FromMinutes(-1)));

        await store.DeleteAsync(schedule.ScheduleId);
        Assert.Null(await store.FindAsync(schedule.ScheduleId));

        // Deleting an already-absent schedule must not throw.
        await store.DeleteAsync(schedule.ScheduleId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SchedulesSurviveRestart_RoundTripThroughNewBridgeInstance(string provider)
    {
        await using var fixture = CreateStore(provider);

        IRecurringTriggerScheduleStore store = NewStore(fixture);
        await store.SaveAsync(NewSchedule("art", "a", nextOffset: TimeSpan.FromMinutes(-1)));
        await store.SaveAsync(NewSchedule("art", "b", nextOffset: TimeSpan.FromMinutes(-1)));

        IRecurringTriggerScheduleStore restarted = NewStore(fixture);
        var due = await restarted.ListDueAsync(Now, limit: 10);

        Assert.Equal(
            new[] { RecurringTriggerSchedule.BuildId("art", "a"), RecurringTriggerSchedule.BuildId("art", "b") },
            due.Select(s => s.ScheduleId));
    }

    private static IRecurringTriggerScheduleStore NewStore(GroundworkDocumentStoreFixture fixture) =>
        new GroundworkRecurringTriggerScheduleStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

    private static RecurringTriggerSchedule NewSchedule(
        string artifactId,
        string executableNodeId,
        TimeSpan nextOffset,
        string expression = "PT5M") => new(
        ScheduleId: RecurringTriggerSchedule.BuildId(artifactId, executableNodeId),
        ArtifactId: artifactId,
        StimulusType: "Timer",
        StimulusHash: $"sha256:{executableNodeId}",
        Kind: RecurringScheduleKind.Interval,
        Expression: expression,
        NextOccurrence: Now + nextOffset,
        CreatedAt: Now);

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);
}
