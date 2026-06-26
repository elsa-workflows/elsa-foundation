using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Stores;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityAvailabilitySettingsHandlerTests
{
    private readonly InMemoryActivityAvailabilitySettingsStore _store = new();

    [Fact]
    public async Task Get_ReturnsDefaultHostScopeWhenNoSettingsExist()
    {
        var handler = new GetActivityAvailabilitySettingsRequestHandler(_store);

        var settings = await handler.Handle(new GetActivityAvailabilitySettings(), CancellationToken.None);

        Assert.Equal(ActivityAvailabilitySettings.HostDefaultScope, settings.Scope);
        Assert.Equal(ActivityAvailabilityManagementMode.AllExcept, settings.Mode);
        Assert.Empty(settings.Rules.ActivityTypes);
        Assert.Empty(settings.Rules.Sets);
    }

    [Fact]
    public async Task SaveThenGet_ReturnsSavedHostScopeSettings()
    {
        var saveHandler = new SaveActivityAvailabilitySettingsCommandHandler(_store);
        var getHandler = new GetActivityAvailabilitySettingsRequestHandler(_store);

        await saveHandler.Handle(new SaveActivityAvailabilitySettings(
            null,
            ActivityAvailabilityManagementMode.Only,
            new ActivityAvailabilityRuleSet
            {
                ActivityTypes = ["Elsa.Test.Activity"],
                Sets = ["Core"]
            }), CancellationToken.None);

        var settings = await getHandler.Handle(new GetActivityAvailabilitySettings(), CancellationToken.None);

        Assert.Equal(ActivityAvailabilitySettings.HostDefaultScope, settings.Scope);
        Assert.Equal(ActivityAvailabilityManagementMode.Only, settings.Mode);
        Assert.Equal(new[] { "Elsa.Test.Activity" }, settings.Rules.ActivityTypes);
        Assert.Equal(new[] { "Core" }, settings.Rules.Sets);
    }
}
