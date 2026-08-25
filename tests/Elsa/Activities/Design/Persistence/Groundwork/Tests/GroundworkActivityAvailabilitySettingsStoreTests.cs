using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkActivityAvailabilitySettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_ReturnsNullWhenNoSettingsExist()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var store = new GroundworkActivityAvailabilitySettingsStore(harness.Store);

        Assert.Null(await store.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope));
    }

    [Fact]
    public async Task SaveAsync_RoundTripsHostDefaultSettings()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var store = new GroundworkActivityAvailabilitySettingsStore(harness.Store);
        await store.SaveAsync(new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.Only,
            Rules = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = ["Elsa.Test.Activity", "Elsa.Missing.Activity"],
                Sets = ["Core", "MissingSet"]
            }
        });

        var settings = await store.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope);
        Assert.NotNull(settings);
        Assert.Equal(ActivityAvailabilitySettings.HostDefaultScope, settings.Scope);
        Assert.Equal(ActivityAvailabilityManagementMode.Only, settings.Mode);
        Assert.Equal(["Elsa.Test.Activity", "Elsa.Missing.Activity"], settings.Rules.ActivityTypes);
        Assert.Equal(["Core", "MissingSet"], settings.Rules.Sets);
    }
}
