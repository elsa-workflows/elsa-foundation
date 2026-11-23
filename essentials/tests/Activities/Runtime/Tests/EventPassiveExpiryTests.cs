using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class EventPassiveExpiryTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task Original_suspension_deadline_survives_native_creation_and_expiry_does_not_wake_the_owner(PersistenceMode mode)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddDays(1));
        var identity = new WorkflowExecutableIdentity("expiry-artifact", "expiry-definition", "expiry-version", "1", "expiry-hash");
        await using var harness = WorkflowExecutionHarness.Create().WithPersistenceMode(mode)
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(clock)).Build(identity, "expiry-run", ["expiry-activity"]);
        var deadline = clock.GetUtcNow().AddMinutes(5);
        await harness.RunAsync(EventWaitExecutable.Create(identity, "expiry", deadline));
        var store = harness.Services.GetRequiredService<IBookmarkStateStore>();
        var bookmark = Assert.Single((await store.ListPageAsync(new(harness.ExecutionId, 10))).Items);
        Assert.Equal(deadline, bookmark.ExpiresAt);
        var owner = Assert.Single((await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListPageAsync(new(harness.ExecutionId, 10))).Items);
        Assert.DoesNotContain("ExpiresAt", JsonSerializer.Serialize(Assert.Single(owner.TriggerRegistrations!)));
        var retained = JsonSerializer.Deserialize<ActivityTriggerRegistration>(JsonSerializer.Serialize(Assert.Single(owner.TriggerRegistrations!)));
        Assert.Equal(JsonSerializer.Serialize(Assert.Single(owner.TriggerRegistrations!)), JsonSerializer.Serialize(retained));
        clock.Advance(TimeSpan.FromMinutes(6));
        var lookup = await harness.Services.GetRequiredService<IBookmarkStimulusLookup>().FindAsync(new(
            harness.ExecutionId, bookmark.StimulusType, bookmark.StimulusHash, clock.GetUtcNow(), bookmark.BookmarkId));
        Assert.NotEqual(BookmarkStimulusLookupStatus.Found, lookup.Status);
        Assert.Equal(bookmark, Assert.Single((await store.ListPageAsync(new(harness.ExecutionId, 10))).Items));
        Assert.Equal(ActivityExecutionStatus.Suspended,
            (await harness.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(harness.ExecutionId, owner.InvocationId))!.Status);
    }

    [Fact]
    public void Retained_legacy_registration_without_expiry_round_trips_without_an_added_null_field()
    {
        var registration = new ActivityTriggerRegistration("registration", "invocation", "resume", new("String"), "Event", "hash", ActivityTriggerDeduplicationPolicy.Once);
        var json = JsonSerializer.Serialize(registration);
        Assert.DoesNotContain("ExpiresAt", json);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<ActivityTriggerRegistration>(json)));
    }
}
