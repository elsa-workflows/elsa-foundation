using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>
/// The authoring catalog lists only addable activities by default: it applies the configured availability options
/// and the saved host management settings to the persisted catalog. Engine intrinsics are never gated, so the
/// assertions look at persisted activities only.
/// </summary>
public sealed class ActivityAuthoringCatalogAvailabilityTests
{
    private const string WriteLineTypeKey = "Elsa.Activities.Primitives.Activities.WriteLine";
    private const string RunJavaScriptTypeKey = "Elsa.Activities.Scripting.Activities.RunJavaScript";

    private readonly ActivityDefinition[] _definitions = [Definition("write-line", WriteLineTypeKey), Definition("run-js", RunJavaScriptTypeKey)];
    private readonly InMemoryActivityAvailabilitySettingsStore _settingsStore = new();

    [Fact]
    public async Task Catalog_applies_the_availability_options()
    {
        var evaluator = new DefaultActivityAvailabilityEvaluator(new ActivityAvailabilityOptions
        {
            Exclude = new ActivityAvailabilityRuleSet { ActivityTypes = [RunJavaScriptTypeKey] }
        });

        Assert.Equal([WriteLineTypeKey], await ListPersistedTypeKeysAsync(evaluator));
    }

    [Fact]
    public async Task Catalog_applies_saved_management_settings_immediately()
    {
        await _settingsStore.SaveAsync(new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.Only,
            Rules = new ActivityAvailabilityRuleSet { ActivityTypes = [WriteLineTypeKey] }
        });

        Assert.Equal([WriteLineTypeKey], await ListPersistedTypeKeysAsync(new DefaultActivityAvailabilityEvaluator(new ActivityAvailabilityOptions())));
    }

    private async Task<string[]> ListPersistedTypeKeysAsync(DefaultActivityAvailabilityEvaluator evaluator)
    {
        var versions = _definitions.Select(definition => new ActivityDefinitionVersion("1.0.0", definition.Id) { Id = $"ver-{definition.Id}" }).ToArray();
        var view = await CatalogReaderFixture.ListAsync(_definitions, versions, availabilityEvaluator: evaluator, settingsStore: _settingsStore);

        return view.Activities.Where(activity => activity.Intrinsic is null).Select(activity => activity.ActivityTypeKey).ToArray();
    }

    private static ActivityDefinition Definition(string id, string activityTypeKey) =>
        new() { Id = id, ActivityTypeKey = activityTypeKey, Category = "Test", DisplayName = activityTypeKey };
}
