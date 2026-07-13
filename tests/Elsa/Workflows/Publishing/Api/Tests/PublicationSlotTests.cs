using Elsa.Workflows.Publishing.Api.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublicationSlotTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryPublicationSlotStore _store = new();

    [Fact]
    public async Task FirstActivationCreatesAuthoritativeSlotAtRevisionOne()
    {
        var result = await _store.TryActivateAsync(
            "definition-1",
            "default",
            "publication-1",
            expectedRevision: 0,
            _now);

        Assert.True(result.Succeeded);
        Assert.Equal("publication-1", result.Slot.ActivePublicationId);
        Assert.Equal(1, result.Slot.Revision);
        Assert.Equal(_now, result.Slot.UpdatedAt);
    }

    [Fact]
    public async Task ReplacementSelectsOneAuthorityAndIncrementsRevision()
    {
        var first = await _store.TryActivateAsync("definition-1", "default", "publication-1", 0, _now);

        var replacement = await _store.TryActivateAsync(
            "definition-1",
            "default",
            "publication-2",
            first.Slot.Revision,
            _now.AddMinutes(1));

        Assert.True(replacement.Succeeded);
        Assert.Equal("publication-2", replacement.Slot.ActivePublicationId);
        Assert.Equal(2, replacement.Slot.Revision);
        Assert.Equal("publication-1", replacement.ReplacedPublicationId);
    }

    [Fact]
    public async Task StaleReplacementLosesWithoutChangingCurrentAuthority()
    {
        var first = await _store.TryActivateAsync("definition-1", "default", "publication-1", 0, _now);
        await _store.TryActivateAsync("definition-1", "default", "publication-winner", first.Slot.Revision, _now.AddMinutes(1));

        var loser = await _store.TryActivateAsync(
            "definition-1",
            "default",
            "publication-loser",
            first.Slot.Revision,
            _now.AddMinutes(2));
        var current = await _store.FindAsync("definition-1", "default");

        Assert.False(loser.Succeeded);
        Assert.Equal("publication-winner", current!.ActivePublicationId);
        Assert.Equal(2, current.Revision);
    }

    [Fact]
    public async Task UnpublishClearsAuthorityAndIncrementsRevision()
    {
        var active = await _store.TryActivateAsync("definition-1", "default", "publication-1", 0, _now);

        var unpublished = await _store.TryUnpublishAsync(
            "definition-1",
            "default",
            active.Slot.Revision,
            _now.AddMinutes(1));

        Assert.True(unpublished.Succeeded);
        Assert.Null(unpublished.Slot.ActivePublicationId);
        Assert.Equal(2, unpublished.Slot.Revision);
        Assert.Equal("publication-1", unpublished.ReplacedPublicationId);
    }

    [Fact]
    public async Task DistinctNamedSlotsCanHoldIndependentAuthorities()
    {
        var blue = await _store.TryActivateAsync("definition-1", "blue", "publication-blue", 0, _now);
        var green = await _store.TryActivateAsync("definition-1", "green", "publication-green", 0, _now);

        Assert.NotEqual(blue.Slot.SlotId, green.Slot.SlotId);
        Assert.Equal("publication-blue", blue.Slot.ActivePublicationId);
        Assert.Equal("publication-green", green.Slot.ActivePublicationId);
    }
}
