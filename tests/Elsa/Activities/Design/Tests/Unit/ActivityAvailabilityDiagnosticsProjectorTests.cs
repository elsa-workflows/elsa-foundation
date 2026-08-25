using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityAvailabilityDiagnosticsProjectorTests
{
    private const string WriteLineTypeKey = "Elsa.Activities.Primitives.Activities.WriteLine";
    private const string SendHttpTypeKey = "Elsa.Activities.Http.Activities.SendHttpRequest";
    private const string RunJavaScriptTypeKey = "Elsa.Activities.Scripting.Activities.RunJavaScript";
    private const string MissingActivityTypeKey = "Elsa.Activities.Missing.Activities.LaterInstalled";

    private readonly ActivityDefinition[] _activities =
    [
        Activity("write-line", WriteLineTypeKey),
        Activity("send-http", SendHttpTypeKey),
        Activity("run-js", RunJavaScriptTypeKey)
    ];

    [Fact]
    public void Project_ReportsAvailableBlockedAndHiddenCatalogActivities()
    {
        var options = new ActivityAvailabilityOptions
        {
            Exclude = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [RunJavaScriptTypeKey]
            }
        };
        var settings = new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.AllExcept,
            Rules = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [SendHttpTypeKey]
            }
        };

        var result = Project(options, settings);

        AssertState(result, WriteLineTypeKey, ActivityAvailabilityDiagnosticState.Available, ActivityAvailabilityPolicyLayer.Catalog);
        AssertState(result, SendHttpTypeKey, ActivityAvailabilityDiagnosticState.HiddenByManagementSettings, ActivityAvailabilityPolicyLayer.ManagementSettings);
        AssertState(result, RunJavaScriptTypeKey, ActivityAvailabilityDiagnosticState.BlockedByHostBaseline, ActivityAvailabilityPolicyLayer.HostBaseline);
    }

    [Fact]
    public void Project_ReportsUnresolvedActivityTypeReferences()
    {
        const string missingHostActivity = "Elsa.Missing.HostActivity";
        const string missingManagementActivity = "Elsa.Missing.ManagementActivity";
        var options = new ActivityAvailabilityOptions
        {
            Include = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [WriteLineTypeKey, missingHostActivity]
            }
        };
        var settings = new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.AllExcept,
            Rules = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [missingManagementActivity]
            }
        };

        var result = Project(options, settings);

        AssertReference(result, missingHostActivity, ActivityAvailabilityDiagnosticState.UnresolvedReference, ActivityAvailabilityPolicyLayer.HostBaseline, ActivityAvailabilityReferenceKind.ActivityType);
        AssertReference(result, missingManagementActivity, ActivityAvailabilityDiagnosticState.UnresolvedReference, ActivityAvailabilityPolicyLayer.ManagementSettings, ActivityAvailabilityReferenceKind.ActivityType);
    }

    [Fact]
    public void Project_ReportsUnresolvedSetReferencesWithoutRemovingUnrelatedActivities()
    {
        var options = new ActivityAvailabilityOptions
        {
            Exclude = new ActivityAvailabilityRuleSet
            {
                Sets = ["MissingHostSet"]
            }
        };
        var settings = new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.Only,
            Rules = new ActivityAvailabilityRuleSet
            {
                Sets = ["MissingManagementSet"]
            }
        };

        var result = Project(options, settings);

        AssertReference(result, "MissingHostSet", ActivityAvailabilityDiagnosticState.UnresolvedReference, ActivityAvailabilityPolicyLayer.HostBaseline, ActivityAvailabilityReferenceKind.ActivitySet);
        AssertReference(result, "MissingManagementSet", ActivityAvailabilityDiagnosticState.UnresolvedReference, ActivityAvailabilityPolicyLayer.ManagementSettings, ActivityAvailabilityReferenceKind.ActivitySet);
        Assert.Equal(3, result.Items.Count(x => x.State == ActivityAvailabilityDiagnosticState.Available));
    }

    [Fact]
    public void Project_UnknownHostIncludeActivityType_DoesNotBlockCatalogActivities()
    {
        var options = new ActivityAvailabilityOptions
        {
            Include = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [MissingActivityTypeKey]
            }
        };

        var result = Project(options);

        AssertReference(result, MissingActivityTypeKey, ActivityAvailabilityDiagnosticState.UnresolvedReference, ActivityAvailabilityPolicyLayer.HostBaseline, ActivityAvailabilityReferenceKind.ActivityType);
        Assert.Equal(3, result.Items.Count(x => x.State == ActivityAvailabilityDiagnosticState.Available));
    }

    [Fact]
    public void Project_ManagementOnlyUnknownActivityType_DoesNotHideCatalogActivities()
    {
        var settings = new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.Only,
            Rules = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [MissingActivityTypeKey]
            }
        };

        var result = Project(new ActivityAvailabilityOptions(), settings);

        AssertReference(result, MissingActivityTypeKey, ActivityAvailabilityDiagnosticState.UnresolvedReference, ActivityAvailabilityPolicyLayer.ManagementSettings, ActivityAvailabilityReferenceKind.ActivityType);
        Assert.Equal(3, result.Items.Count(x => x.State == ActivityAvailabilityDiagnosticState.Available));
    }

    [Fact]
    public void Project_ReportsHostDefinedSets()
    {
        var options = new ActivityAvailabilityOptions
        {
            Sets =
            {
                ["Core"] = [WriteLineTypeKey, SendHttpTypeKey]
            }
        };

        var result = Project(options);

        var set = Assert.Single(result.Sets);
        Assert.Equal("Core", set.Name);
        Assert.Equal(new[] { WriteLineTypeKey, SendHttpTypeKey }, set.ActivityTypeKeys);
    }

    [Fact]
    public async Task Handler_LoadsSettingsAndDoesNotMutateStoredRules()
    {
        var store = new InMemoryActivityAvailabilitySettingsStore();
        await store.SaveAsync(new ActivityAvailabilitySettings
        {
            Mode = ActivityAvailabilityManagementMode.Only,
            Rules = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [WriteLineTypeKey],
                Sets = ["MissingManagementSet"]
            }
        });
        var operations = new ActivityAvailabilityOperations(
            new StubActivityDefinitionStore(_activities),
            store,
            new DefaultActivityAvailabilityDiagnosticsProjector(),
            Options.Create(new ActivityAvailabilityOptions()));

        var result = await operations.ListDiagnosticsAsync(new ListActivityAvailabilityDiagnostics(), CancellationToken.None);
        var savedSettings = await store.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope);

        AssertState(result, WriteLineTypeKey, ActivityAvailabilityDiagnosticState.Available, ActivityAvailabilityPolicyLayer.Catalog);
        AssertState(result, SendHttpTypeKey, ActivityAvailabilityDiagnosticState.HiddenByManagementSettings, ActivityAvailabilityPolicyLayer.ManagementSettings);
        Assert.NotNull(savedSettings);
        Assert.Equal(new[] { WriteLineTypeKey }, savedSettings.Rules.ActivityTypes);
        Assert.Equal(new[] { "MissingManagementSet" }, savedSettings.Rules.Sets);
    }

    private ActivityAvailabilityDiagnostics Project(ActivityAvailabilityOptions options, ActivityAvailabilitySettings? settings = null)
    {
        var projector = new DefaultActivityAvailabilityDiagnosticsProjector();
        return projector.Project(_activities, options, settings);
    }

    private static void AssertState(
        ActivityAvailabilityDiagnostics diagnostics,
        string activityTypeKey,
        ActivityAvailabilityDiagnosticState state,
        ActivityAvailabilityPolicyLayer layer)
    {
        var entry = Assert.Single(diagnostics.Items.Where(x => x.ActivityTypeKey == activityTypeKey));
        Assert.Equal(state, entry.State);
        Assert.Equal(layer, entry.Layer);
        Assert.Equal(ActivityAvailabilityReferenceKind.ActivityType, entry.ReferenceKind);
        Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
    }

    private static void AssertReference(
        ActivityAvailabilityDiagnostics diagnostics,
        string referenceName,
        ActivityAvailabilityDiagnosticState state,
        ActivityAvailabilityPolicyLayer layer,
        ActivityAvailabilityReferenceKind kind)
    {
        var entry = Assert.Single(diagnostics.Items.Where(x => x.ReferenceName == referenceName));
        Assert.Equal(state, entry.State);
        Assert.Equal(layer, entry.Layer);
        Assert.Equal(kind, entry.ReferenceKind);
        Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
    }

    private static ActivityDefinition Activity(string id, string activityTypeKey) =>
        new()
        {
            Id = id,
            ActivityTypeKey = activityTypeKey,
            Category = "Test",
            DisplayName = activityTypeKey
        };

    private sealed class StubActivityDefinitionStore(IReadOnlyList<ActivityDefinition> activities) : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(activities);

        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");

        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only supports ListAsync.");
    }
}
