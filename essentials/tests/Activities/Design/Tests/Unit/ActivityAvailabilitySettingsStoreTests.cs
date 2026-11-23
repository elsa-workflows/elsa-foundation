using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Stores;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityAvailabilitySettingsStoreTests
{
    private readonly InMemoryActivityAvailabilitySettingsStore _store = new();

    [Fact]
    public async Task LoadAsync_ReturnsNullWhenNoSettingsExist()
    {
        var settings = await _store.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope);

        Assert.Null(settings);
    }

    [Fact]
    public async Task SaveAsync_PreservesScopeModeAndUnresolvedReferences()
    {
        var settings = new ActivityAvailabilitySettings
        {
            Scope = ActivityAvailabilitySettings.HostDefaultScope,
            Mode = ActivityAvailabilityManagementMode.Only,
            Rules = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = ["missing.activity"],
                Sets = ["MissingSet"]
            }
        };

        await _store.SaveAsync(settings);

        var loaded = await _store.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope);
        Assert.NotNull(loaded);
        Assert.Equal(ActivityAvailabilitySettings.HostDefaultScope, loaded.Scope);
        Assert.Equal(ActivityAvailabilityManagementMode.Only, loaded.Mode);
        Assert.Equal(new[] { "missing.activity" }, loaded.Rules.ActivityTypes);
        Assert.Equal(new[] { "MissingSet" }, loaded.Rules.Sets);
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingSettings()
    {
        await _store.SaveAsync(new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.Only,
            Rules = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = ["first"]
            }
        });
        await _store.SaveAsync(new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.AllExcept,
            Rules = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = ["second"]
            }
        });

        var loaded = await _store.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope);
        Assert.NotNull(loaded);
        Assert.Equal(ActivityAvailabilityManagementMode.AllExcept, loaded.Mode);
        Assert.Equal(new[] { "second" }, loaded.Rules.ActivityTypes);
    }
}
