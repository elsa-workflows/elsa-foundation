using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Core.Stores;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ListDefinitionsRequestHandlerTests
{
    private const string WriteLineTypeKey = "Elsa.Activities.Primitives.Activities.WriteLine";
    private const string RunJavaScriptTypeKey = "Elsa.Activities.Scripting.Activities.RunJavaScript";

    [Fact]
    public async Task Handle_AppliesActivityAvailabilityPolicyToPickerResults()
    {
        var lookup = new StubActivityDefinitionLookup(
        [
            Activity("write-line", WriteLineTypeKey),
            Activity("run-js", RunJavaScriptTypeKey)
        ]);
        var evaluator = new DefaultActivityAvailabilityEvaluator(new ActivityAvailabilityOptions
        {
            Exclude = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [RunJavaScriptTypeKey]
            }
        });
        var handler = new ListDefinitionsRequestHandler(lookup, evaluator, new InMemoryActivityAvailabilitySettingsStore());

        var result = await handler.Handle(new ListDefinitions(null, null, null, null, null, null), CancellationToken.None);

        var definition = Assert.Single(result);
        Assert.Equal(WriteLineTypeKey, definition.ActivityTypeKey);
    }

    [Fact]
    public async Task Handle_AppliesSavedManagementSettingsImmediately()
    {
        var lookup = new StubActivityDefinitionLookup(
        [
            Activity("write-line", WriteLineTypeKey),
            Activity("run-js", RunJavaScriptTypeKey)
        ]);
        var settingsStore = new InMemoryActivityAvailabilitySettingsStore();
        await settingsStore.SaveAsync(new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.Only,
            Rules = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [WriteLineTypeKey]
            }
        });
        var handler = new ListDefinitionsRequestHandler(
            lookup,
            new DefaultActivityAvailabilityEvaluator(new ActivityAvailabilityOptions()),
            settingsStore);

        var result = await handler.Handle(new ListDefinitions(null, null, null, null, null, null), CancellationToken.None);

        var definition = Assert.Single(result);
        Assert.Equal(WriteLineTypeKey, definition.ActivityTypeKey);
    }

    private static ActivityDefinitionModel Activity(string id, string activityTypeKey) =>
        new(id, activityTypeKey, "Test", activityTypeKey, null);

    private sealed class StubActivityDefinitionLookup(IReadOnlyCollection<IActivityDefinition> activities) : IActivityDefinitionLookup
    {
        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListDefinitions.");

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(
            string? id = null,
            string? category = null,
            string? searchTerm = null,
            string? displayName = null,
            string? description = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<IActivityDefinition>>(activities);

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListDefinitions.");

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListDefinitions.");
    }
}
