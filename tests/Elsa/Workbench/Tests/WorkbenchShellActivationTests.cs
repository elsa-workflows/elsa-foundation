using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// Starts every stock Workbench composition as a real process and checks that it activates and serves requests.
/// Activation is a whole-composition property: a feature registration, a startup task, an appsettings edit, or a
/// Groundwork schema rule can each break it from outside any one domain. On 2026-08-12 main could not activate for nine
/// hours because nothing in the pull-request gate started the host.
/// </summary>
public sealed class WorkbenchShellActivationTests
{
    /// <summary>
    /// Supplied by packages in the Nuplane directory feed (<c>packages/</c>), which is empty in a checkout. CShells
    /// activates the shell without them and logs a warning, which is also how it treats a misspelled feature name.
    /// </summary>
    private static readonly string[] FeedSuppliedFeatures = ["SampleNuplaneActivities", "WeatherForecastSample"];

    /// <summary>Capabilities every stock composition must run, not merely list.</summary>
    private static readonly string[] RequiredFeatures =
    [
        // Flowchart root activities resolve runtime services from this feature.
        "ActivitiesFlowchart",
        // Activity Design advertises the graph authoring provider only when graph design is composed.
        "ActivitiesDesignApi",
        "ActivitiesGraphDesign",
        "ActivitiesGraphRuntime",
        // Publishing and serving HTTP-triggered workflows.
        "ActivitiesHttp",
        // The publish transport and the engine it depends on. CShells would auto-enable an unlisted engine, but
        // GET /modularity/features would then report it disabled while it runs.
        "WorkflowsPublishingApi",
        "WorkflowsPublishing",
        // The dashboard's dependencies.
        "WorkflowDesignValidations",
        "WorkflowsRuntimeResumption",
        // One Groundwork provider connection, with every persistence lane enabled by its own feature.
        "GroundworkProviderSqlite",
        "GroundworkWorkflowRuntime",
        "ActivitiesDesignGroundworkPersistence",
        "WorkflowsDesignGroundworkPersistence",
        "WorkflowsRuntimeDistributedGroundworkPersistence",
        "WorkflowsPublishingGroundwork",
        "GroundworkWorkflowDashboard"
    ];

    /// <summary>Features a stock shell may leave unlisted that must still be in the runtime catalog.</summary>
    private static readonly string[] CatalogedFeatures =
    [
        // ActivitiesHttp depends on its route-table projection by feature name, and a name cannot make an assembly
        // that is absent from the host discoverable. A shell that does not list it gets it as a dependency.
        "WorkflowsRuntimeHttp",
        // File-based workflow deployment (spec 147) is opt-in: it needs a source and a path to register.
        "JsonWorkflowReconciliation"
    ];

    public static TheoryData<string> Shells => new(WorkbenchShell.All.Keys);

    [Theory]
    [MemberData(nameof(Shells))]
    public async Task Stock_shell_activates_and_serves_requests(string shellName)
    {
        var shell = WorkbenchShell.All[shellName];
        await using var workbench = await WorkbenchProcess.StartAsync(shell);

        // Liveness is answered at the process root, outside shell routing.
        var live = await workbench.Client.GetFromJsonAsync<Health>("/health/live");
        Assert.Equal("live", live!.Status);

        // A shell-routed request runs the authentication stack: protected, not broken. A shell can report ready and
        // still fail every request here, for example when Production has no token signing key.
        using var anonymous = await workbench.Client.GetAsync("/modularity/features");
        Assert.True(
            anonymous.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"An anonymous shell request returned {(int)anonymous.StatusCode}. Host output:{Environment.NewLine}{workbench.Output}");

        var catalog = (await workbench.ReadFeatureCatalogAsync()).ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        bool Runs(string feature) => catalog.TryGetValue(feature, out var entry) && entry.Runs;

        var notRunning = shell.ListedFeatures().Except(FeedSuppliedFeatures).Where(feature => !Runs(feature)).Order().ToList();
        Assert.True(notRunning.Count == 0, $"{shell.Name} lists features the host does not run: {string.Join(", ", notRunning)}.");

        var missing = RequiredFeatures.Where(feature => !Runs(feature)).ToList();
        Assert.True(missing.Count == 0, $"{shell.Name} does not run: {string.Join(", ", missing)}.");

        Assert.All(CatalogedFeatures, feature => Assert.True(
            catalog.TryGetValue(feature, out var entry) && entry.SourceKind == "runtime",
            $"{feature} is not in the runtime catalog."));

        // The stock shells run the burst-coalesced checkpoint policy and the bounded executable cache that the HTTP
        // workflow performance budget is measured against.
        AssertSetting(catalog, "WorkflowsRuntimeCheckpointPersistence", "Mode", "Coalesced");
        AssertSetting(catalog, "WorkflowsRuntimeCheckpointPersistence", "MaxSegmentCheckpoints", "50");
        AssertSetting(catalog, "GroundworkWorkflowRuntime", "CacheWorkflowExecutables", "true");
        AssertSetting(catalog, "GroundworkWorkflowRuntime", "WorkflowExecutableCacheCapacity", "256");
    }

    private static void AssertSetting(IReadOnlyDictionary<string, CatalogFeature> catalog, string feature, string setting, string expected)
    {
        Assert.True(
            catalog[feature].Configuration.TryGetValue(setting, out var value),
            $"{feature} is running without a bound {setting} setting.");
        Assert.Equal(expected, value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText());
    }

    private sealed record Health(string Status);
}
