using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Core.Stores;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityAvailabilitySettingsHandlerTests
{
    private readonly InMemoryActivityAvailabilitySettingsStore _store = new();
    private readonly ActivityAvailabilityOperations _operations;

    public ActivityAvailabilitySettingsHandlerTests() =>
        // The settings operations consult only the settings store; the diagnostics-only
        // dependencies are never reached by these tests.
        _operations = new ActivityAvailabilityOperations(
            definitionStore: null!,
            _store,
            new DefaultActivityAvailabilityDiagnosticsProjector(),
            Options.Create(new ActivityAvailabilityOptions()));

    [Fact]
    public async Task Get_ReturnsDefaultHostScopeWhenNoSettingsExist()
    {
        var settings = await _operations.GetSettingsAsync(new GetActivityAvailabilitySettings(), CancellationToken.None);

        Assert.Equal(ActivityAvailabilitySettings.HostDefaultScope, settings.Scope);
        Assert.Equal(ActivityAvailabilityManagementMode.AllExcept, settings.Mode);
        Assert.Empty(settings.Rules.ActivityTypes);
        Assert.Empty(settings.Rules.Sets);
    }

    [Fact]
    public async Task SaveThenGet_ReturnsSavedHostScopeSettings()
    {
        await _operations.SaveSettingsAsync(new SaveActivityAvailabilitySettings(
            null,
            ActivityAvailabilityManagementMode.Only,
            new ActivityAvailabilityRuleSet
            {
                ActivityTypes = ["Elsa.Test.Activity"],
                Sets = ["Core"]
            }), CancellationToken.None);

        var settings = await _operations.GetSettingsAsync(new GetActivityAvailabilitySettings(), CancellationToken.None);

        Assert.Equal(ActivityAvailabilitySettings.HostDefaultScope, settings.Scope);
        Assert.Equal(ActivityAvailabilityManagementMode.Only, settings.Mode);
        Assert.Equal(new[] { "Elsa.Test.Activity" }, settings.Rules.ActivityTypes);
        Assert.Equal(new[] { "Core" }, settings.Rules.Sets);
    }
}
