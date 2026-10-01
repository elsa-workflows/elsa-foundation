using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;
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
    public async Task ClaimDue_ClaimsOnlyDue_OrderedByNextThenId()
    {
        await _store.SaveAsync(Schedule("future", Now.AddMinutes(10)));
        await _store.SaveAsync(Schedule("b-due", Now.AddMinutes(-1)));
        await _store.SaveAsync(Schedule("a-due", Now.AddMinutes(-1)));

        var claims = await _store.ClaimDueAsync(Claim(10));

        Assert.Equal(new[] { "a-due", "b-due" }, claims.Select(claim => claim.Schedule.ScheduleId).ToArray());
    }

    [Fact]
    public async Task ClaimDue_RespectsLimit()
    {
        for (var i = 0; i < 5; i++)
            await _store.SaveAsync(Schedule($"s{i}", Now.AddMinutes(-1)));

        var claims = await _store.ClaimDueAsync(Claim(2));

        Assert.Equal(2, claims.Count);
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

    [Fact]
    public async Task DeleteByArtifact_RemovesTheActivationProjectionStateOfTheDeletedRows()
    {
        await _store.PrepareActivationAsync("act-1", [Schedule("s1", Now) with { ActivationId = "act-1", SlotId = "default" }]);
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await _store.FindActivationStateAsync("act-1"));

        await _store.DeleteByArtifactAsync("artifact-1");

        Assert.Equal(WorkflowActivationProjectionState.Missing, await _store.FindActivationStateAsync("act-1"));
    }

    private static RecurringTriggerOccurrenceClaimRequest Claim(int limit) => new("pump", Now, TimeSpan.FromMinutes(1), limit);

    private static RecurringTriggerSchedule Schedule(string id, DateTimeOffset next, string artifactId = "artifact-1") => new(
        ScheduleId: id,
        ArtifactId: artifactId,
        ExecutableNodeId: $"node-{id}",
        StimulusType: "Timer",
        StimulusHash: $"hash-{id}",
        Kind: RecurringScheduleKind.Interval,
        Expression: "PT5M",
        NextOccurrence: next,
        CreatedAt: Now);
}
