using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class InMemoryRecurringTriggerScheduleStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryRecurringTriggerScheduleStore _store = new();

    [Fact]
    public async Task Save_IsUpsert_RepublishRewritesCursor()
    {
        await _store.SaveAsync(Schedule("s1", Now.AddMinutes(5)));
        await _store.SaveAsync(Schedule("s1", Now.AddMinutes(1)));

        var found = await _store.FindAsync("s1");
        Assert.Equal(Now.AddMinutes(1), found!.NextOccurrence);
    }

    [Fact]
    public async Task ListDue_ReturnsOnlyDue_OrderedByNextThenId()
    {
        await _store.SaveAsync(Schedule("future", Now.AddMinutes(10)));
        await _store.SaveAsync(Schedule("b-due", Now.AddMinutes(-1)));
        await _store.SaveAsync(Schedule("a-due", Now.AddMinutes(-1)));

        var due = await _store.ListDueAsync(Now, 10);

        Assert.Equal(new[] { "a-due", "b-due" }, due.Select(s => s.ScheduleId).ToArray());
    }

    [Fact]
    public async Task ListDue_RespectsLimit()
    {
        for (var i = 0; i < 5; i++)
            await _store.SaveAsync(Schedule($"s{i}", Now.AddMinutes(-1)));

        var due = await _store.ListDueAsync(Now, 2);

        Assert.Equal(2, due.Count);
    }

    [Fact]
    public async Task TryAdvance_ClaimsOccurrence_WhenCursorMatches()
    {
        await _store.SaveAsync(Schedule("s1", Now));

        var claimed = await _store.TryAdvanceAsync("s1", Now, Now.AddMinutes(5));

        Assert.True(claimed);
        Assert.Equal(Now.AddMinutes(5), (await _store.FindAsync("s1"))!.NextOccurrence);
    }

    [Fact]
    public async Task TryAdvance_Fails_WhenCursorAlreadyMoved_SoOccurrenceFiresAtMostOnce()
    {
        await _store.SaveAsync(Schedule("s1", Now));

        var first = await _store.TryAdvanceAsync("s1", Now, Now.AddMinutes(5));
        var second = await _store.TryAdvanceAsync("s1", Now, Now.AddMinutes(5));

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task TryAdvance_Fails_WhenScheduleMissing()
    {
        Assert.False(await _store.TryAdvanceAsync("ghost", Now, Now.AddMinutes(5)));
    }

    [Fact]
    public async Task DeleteByArtifact_RemovesOnlyThatArtifactsSchedules()
    {
        await _store.SaveAsync(Schedule("a1", Now, artifactId: "art-A"));
        await _store.SaveAsync(Schedule("a2", Now, artifactId: "art-A"));
        await _store.SaveAsync(Schedule("b1", Now, artifactId: "art-B"));

        await _store.DeleteByArtifactAsync("art-A");

        Assert.Null(await _store.FindAsync("a1"));
        Assert.Null(await _store.FindAsync("a2"));
        Assert.NotNull(await _store.FindAsync("b1"));
    }

    [Fact]
    public async Task Delete_RemovesSingleSchedule()
    {
        await _store.SaveAsync(Schedule("s1", Now));

        await _store.DeleteAsync("s1");

        Assert.Null(await _store.FindAsync("s1"));
    }

    private static RecurringTriggerSchedule Schedule(string id, DateTimeOffset next, string artifactId = "artifact-1") => new(
        ScheduleId: id,
        ArtifactId: artifactId,
        StimulusType: "Timer",
        StimulusHash: $"hash-{id}",
        Kind: RecurringScheduleKind.Interval,
        Expression: "PT5M",
        NextOccurrence: next,
        CreatedAt: Now);
}
