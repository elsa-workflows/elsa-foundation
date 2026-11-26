using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Fact]
    public async Task Resource_notification_observes_committed_state_before_apply_acknowledgement()
    {
        var fixture = new Fixture();
        var request = await ReviewedResourceChange(fixture);
        fixture.Events.ObserveCommit = notification =>
        {
            Assert.Equal(1, fixture.Store.Writes);
            Assert.Equal(fixture.Access.Scope, notification.Scope);
            Assert.Equal(fixture.Store.Resource.Trigger.Id, notification.TriggerId);
            Assert.Equal(fixture.Store.Resource.Trigger.TypeKey, notification.TypeKey);
            Assert.Equal(fixture.Store.Resource.Trigger.Revision, notification.Revision);
        };
        var result = await fixture.Management.ApplyAsync("trigger-a", request, default);
        Assert.Equal("Applied", result.Disposition);
        Assert.Single(fixture.Events.Committed);
    }

    [Fact]
    public async Task Authorized_replay_repairs_a_failed_projection_without_committing_the_mutation_twice()
    {
        var fixture = new Fixture();
        var request = await ReviewedResourceChange(fixture);
        fixture.Events.ObserveCommit = _ => throw new TriggerOperationException("unavailable", "Committed; local projection unavailable.", innerException: new IOException("Owned projection failure"));
        var failure = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.ApplyAsync("trigger-a", request, default).AsTask());
        Assert.Equal("unavailable", failure.Code);
        Assert.IsType<IOException>(failure.InnerException);
        Assert.Equal(1, fixture.Store.Writes);
        fixture.Events.ObserveCommit = null;
        var replay = await fixture.Management.ApplyAsync("trigger-a", request, default);
        Assert.Equal("AlreadyApplied", replay.Disposition);
        Assert.Equal(1, fixture.Store.Writes);
        Assert.Equal(2, fixture.Events.Committed.Count);
        Assert.Equal(fixture.Events.Committed[0].Revision, fixture.Events.Committed[1].Revision);
    }

    [Fact]
    public async Task Failed_resource_commit_emits_no_projection_notification()
    {
        var fixture = new Fixture(new RuntimeCapability(true, true));
        var request = await ReviewedResourceChange(fixture);
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Management.ApplyAsync("trigger-a", request, default).AsTask());
        Assert.Equal(0, fixture.Store.Writes);
        Assert.Empty(fixture.Events.Committed);
    }

    [Fact]
    public async Task Replay_authorization_precedes_projection_notification()
    {
        var fixture = new Fixture();
        var request = await ReviewedResourceChange(fixture);
        await fixture.Management.ApplyAsync("trigger-a", request, default);
        fixture.Access.ReadResources = false;
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.ApplyAsync("trigger-a", request, default).AsTask());
        Assert.Single(fixture.Events.Committed);
        Assert.Equal(1, fixture.Store.Writes);
    }

    private static async Task<JsonElement> ReviewedResourceChange(Fixture fixture)
    {
        var command = Element("""{"kind":"AddSubscription","target":{"definitionId":"definition-a","slotName":"default"},"mappings":[],"enabled":false,"invocationRequest":"None"}""");
        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default);
        Assert.True(preview.CanApply);
        return JsonSerializer.SerializeToElement(new { requestId = "resource-projection", previewId = preview.PreviewId, expectedRevision = 1, command });
    }
}
