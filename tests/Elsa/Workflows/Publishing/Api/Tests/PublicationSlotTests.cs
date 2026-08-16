using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// The slot-authority contract, unchanged in substance. Its subject moved from publishing's deleted
/// <c>InMemoryPublicationSlotStore</c> to the neutral <see cref="InMemoryWorkflowActivationAuthority"/>
/// (FR-B-006): one ledger per engine. Every assertion below is the one it made before the move.
/// </summary>
public sealed class PublicationSlotTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowActivationAuthority _store = new();

    [Fact]
    public async Task FirstActivationCreatesAuthoritativeSlotAtRevisionOne()
    {
        var result = await _store.TryActivateAsync(Request("publication-1", expectedRevision: 0, _now));

        Assert.True(result.Succeeded);
        Assert.Equal("publication-1", result.Slot.ActiveActivationId);
        Assert.Equal(1, result.Slot.Revision);
        Assert.Equal(_now, result.Slot.UpdatedAt);
    }

    [Fact]
    public async Task ReplacementSelectsOneAuthorityAndIncrementsRevision()
    {
        var first = await _store.TryActivateAsync(Request("publication-1", 0, _now));

        var replacement = await _store.TryActivateAsync(
            Request("publication-2", first.Slot.Revision, _now.AddMinutes(1)));

        Assert.True(replacement.Succeeded);
        Assert.Equal("publication-2", replacement.Slot.ActiveActivationId);
        Assert.Equal(2, replacement.Slot.Revision);
        Assert.Equal("publication-1", replacement.ReplacedActivationId);
    }

    [Fact]
    public async Task StaleReplacementLosesWithoutChangingCurrentAuthority()
    {
        var first = await _store.TryActivateAsync(Request("publication-1", 0, _now));
        await _store.TryActivateAsync(Request("publication-winner", first.Slot.Revision, _now.AddMinutes(1)));

        var loser = await _store.TryActivateAsync(
            Request("publication-loser", first.Slot.Revision, _now.AddMinutes(2)));
        var current = await _store.FindAsync("definition-1", "default");

        Assert.False(loser.Succeeded);
        Assert.Equal("publication-winner", current!.ActiveActivationId);
        Assert.Equal(2, current.Revision);
    }

    [Fact]
    public async Task UnpublishClearsAuthorityAndIncrementsRevision()
    {
        var active = await _store.TryActivateAsync(Request("publication-1", 0, _now));

        var unpublished = await _store.TryDeactivateAsync(
            "definition-1",
            "default",
            WorkflowActivationSource.Publishing,
            active.Slot.Revision,
            _now.AddMinutes(1));

        Assert.True(unpublished.Succeeded);
        Assert.Null(unpublished.Slot.ActiveActivationId);
        Assert.Equal(2, unpublished.Slot.Revision);
        Assert.Equal("publication-1", unpublished.ReplacedActivationId);
    }

    [Fact]
    public async Task DistinctNamedSlotsCanHoldIndependentAuthorities()
    {
        var blue = await _store.TryActivateAsync(Request("publication-blue", 0, _now, "blue"));
        var green = await _store.TryActivateAsync(Request("publication-green", 0, _now, "green"));

        Assert.NotEqual(blue.Slot.SlotId, green.Slot.SlotId);
        Assert.Equal("publication-blue", blue.Slot.ActiveActivationId);
        Assert.Equal("publication-green", green.Slot.ActiveActivationId);
    }

    private static WorkflowActivationSlotRequest Request(
        string activationId,
        long expectedRevision,
        DateTimeOffset updatedAt,
        string slotName = "default") =>
        new("definition-1", slotName, activationId, WorkflowActivationSource.Publishing, expectedRevision, updatedAt);
}
