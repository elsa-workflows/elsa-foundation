using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkActivityAvailabilitySettingsStoreTests
{
    private readonly GroundworkActivityAvailabilitySettingsStore _store =
        new(new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create()));

    [Fact]
    public async Task LoadAsync_ReturnsNullWhenNoSettingsExist()
    {
        var settings = await _store.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope);

        Assert.Null(settings);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsHostDefaultSettings()
    {
        await _store.SaveAsync(new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.Only,
            Rules = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = ["Elsa.Test.Activity"],
                Sets = ["Core"]
            }
        });

        var settings = await _store.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope);

        Assert.NotNull(settings);
        Assert.Equal(ActivityAvailabilitySettings.HostDefaultScope, settings.Scope);
        Assert.Equal(ActivityAvailabilityManagementMode.Only, settings.Mode);
        Assert.Equal(new[] { "Elsa.Test.Activity" }, settings.Rules.ActivityTypes);
        Assert.Equal(new[] { "Core" }, settings.Rules.Sets);
    }
}
