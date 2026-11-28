using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// Starts every stock Workbench composition as a real process and checks that it activates and serves requests.
/// Activation is a whole-composition property: a feature registration, a startup task, an appsettings edit, or a
/// schema rule can each break it from outside any one domain. On 2026-08-12 main could not activate for nine
/// hours because nothing in the pull-request gate started the host.
/// </summary>
public sealed class WorkbenchShellActivationTests
{
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
        "KernelActivitiesHttp",
        "ActorHttpConnections",
        "WorkflowsPublishingApi",
        "WorkflowsPublishing",
        "WorkflowDesignValidations",
        "WorkflowsRuntimeKernelHosting",
        "WorkflowsRuntimeKernelRetainedApi",
        "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence",
        "WorkflowsRuntimePublicationEntityFrameworkCorePersistence",
        "WorkflowsRuntimeTestScopeEntityFrameworkCorePersistence",
        "ActivitiesDesignEntityFrameworkCore",
        "WorkflowsDesignEntityFrameworkCore",
        "WorkflowsPublishingEntityFrameworkCore",
        "WorkflowsDashboardPortfolioEntityFrameworkCore",
        "KernelWorkflowDashboardQueries"
    ];

    /// <summary>Features a stock shell may leave unlisted that must still be in the runtime catalog.</summary>
    private static readonly string[] CatalogedFeatures =
    [
        // ActivitiesHttp depends on its route-table projection by feature name, and a name cannot make an assembly
        // that is absent from the host discoverable. A shell that does not list it gets it as a dependency.
        "WorkflowsRuntimeHttp",
        // File-based workflow deployment is opt-in: it needs a source and a path to register.
        "JsonWorkflowReconciliation"
    ];

    public static TheoryData<string> Shells => new(WorkbenchShell.All.Keys);

    [Fact]
    public async Task Parallel_stock_shells_bind_distinct_ports_and_serve_requests()
    {
        var launches = new[]
        {
            WorkbenchProcess.StartAsync(WorkbenchShell.Development),
            WorkbenchProcess.StartAsync(WorkbenchShell.Development)
        };

        try
        {
            var processes = await Task.WhenAll(launches);
            Assert.NotEqual(processes[0].Client.BaseAddress!.Port, processes[1].Client.BaseAddress!.Port);
            foreach (var process in processes)
            {
                Assert.InRange(process.Client.BaseAddress!.Port, 1, 65535);
                var live = await process.Client.GetFromJsonAsync<Health>("/health/live");
                Assert.Equal("live", live!.Status);
            }
        }
        finally
        {
            foreach (var launch in launches.Where(launch => launch.IsCompletedSuccessfully))
                await (await launch).DisposeAsync();
        }
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public async Task Stock_shell_activates_and_serves_requests(string shellName)
    {
        var shell = WorkbenchShell.All[shellName];
        await using var workbench = await WorkbenchProcess.StartAsync(shell);

        // Liveness is answered at the process root, outside shell routing.
        var live = await workbench.Client.GetFromJsonAsync<Health>("/health/live");
        Assert.Equal("live", live!.Status);

        // A shell-routed request runs the authentication stack: protected, not broken. Lazily built authentication
        // state can let a shell report ready and still fail every request here.
        using var anonymous = await workbench.Client.GetAsync("/modularity/features");
        Assert.True(
            anonymous.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"An anonymous shell request returned {(int)anonymous.StatusCode}. Host output:{Environment.NewLine}{workbench.Output}");

        var catalog = (await workbench.ReadFeatureCatalogAsync()).ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        bool Runs(string feature) => catalog.TryGetValue(feature, out var entry) && entry.Runs;

        // CShells activates a shell that lists a feature missing from the runtime catalog and only logs a warning, which
        // is also how it treats a misspelled name, so every listed feature must run. The stock shells list no feature
        // that only a Nuplane feed package supplies, because the feed is empty in a checkout.
        var notRunning = shell.ListedFeatures().Where(feature => !Runs(feature)).Order().ToList();
        Assert.True(notRunning.Count == 0, $"{shell.Name} lists features the host does not run: {string.Join(", ", notRunning)}.");

        var missing = RequiredFeatures.Where(feature => !Runs(feature)).ToList();
        Assert.True(missing.Count == 0, $"{shell.Name} does not run: {string.Join(", ", missing)}.");

        Assert.All(CatalogedFeatures, feature => Assert.True(
            catalog.TryGetValue(feature, out var entry) && entry.SourceKind == "runtime",
            $"{feature} is not in the runtime catalog."));

        Assert.False(Runs("ConnectionsWorkflows"));
        Assert.False(Runs("WorkflowsRuntimeApi"));
        Assert.False(Runs("WorkflowsRuntimeCheckpointPersistence"));
        Assert.False(Runs("WorkflowsRuntimeResumption"));
        Assert.False(Runs("WorkflowsRuntimeDistributed"));
        AssertSetting(catalog, "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence", "CacheWorkflowExecutables", "true");
        AssertSetting(catalog, "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence", "WorkflowExecutableCacheCapacity", "256");
        Assert.True(File.Exists(Path.Combine(workbench.ContentRoot, "elsa-workbench-actors.db")));

        if (shell == WorkbenchShell.Development)
        {
            var active = await ReadEffectiveFeatureClosureAsync(workbench);
            Assert.Contains("ActorHttpConnections", active);
            Assert.Contains("DecisionsDesign", active);
            Assert.Contains("DecisionsApi", active);
            Assert.Contains("DecisionsEntityFrameworkCore", active);
            Assert.DoesNotContain("DecisionsWorkflowsDesign", active);
            Assert.DoesNotContain("ConnectionsWorkflows", active);
            Assert.DoesNotContain("WorkflowsRuntimeDistributed", active);
            Assert.DoesNotContain("WorkflowsRuntimeCheckpointPersistence", active);
            Assert.DoesNotContain("WorkflowsRuntimeResumption", active);
        }
    }

    [Fact]
    public async Task Explicit_core_preview_keeps_the_mixed_workflow_shell()
    {
        var shell = WorkbenchShell.Development with
        {
            Settings = new Dictionary<string, string>
            {
                ["Elsa:CorePreview:Enabled"] = "true",
                ["Elsa:CorePreview:Publications"] = "true",
                ["Elsa:CorePreview:Provider"] = "SQLite",
                ["Elsa:CorePreview:SqlitePath"] = "elsa-workbench-preview.db"
            }
        };

        await using var workbench = await WorkbenchProcess.StartAsync(shell);
        using var diagnostics = await workbench.Client.GetAsync("/runtime/workflows/diagnostics/settings");
        Assert.True(diagnostics.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"The explicit preview did not map the legacy Runtime API. Host output:{Environment.NewLine}{workbench.Output}");
        var active = await ReadEffectiveFeatureClosureAsync(workbench);
        Assert.Contains("ConnectionsWorkflows", active);
        Assert.Contains("DecisionsWorkflowsDesign", active);
        Assert.Contains("WorkflowsRuntimeResumption", active);
        Assert.DoesNotContain("ActorHttpConnections", active);
        Assert.DoesNotContain("WorkflowsRuntimeDistributed", active);
        Assert.DoesNotContain("WorkflowsRuntimeDistributedEntityFrameworkCorePersistence", active);
        Assert.DoesNotContain("WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence", active);
        Assert.True(File.Exists(Path.Combine(workbench.ContentRoot, "elsa-workbench-preview.db")));
        Assert.False(File.Exists(Path.Combine(workbench.ContentRoot, "elsa-workbench-actors.db")));

        const string publishedMarker = "mixed-preview-published-execute";
        const string draftMarker = "mixed-preview-draft-test-run";
        await NativeHostingScenario.LoginAsync(workbench);
        var writeLine = await NativeHostingScenario.ActivityVersionAsync(workbench, "Elsa.Activities.Primitives.Activities.WriteLine");
        var publishedVersion = await SubmitAsync("Mixed preview published execution", publishedMarker);
        var published = await NativeHostingScenario.PostAsync(workbench, $"/publishing/workflows/{publishedVersion}/publish", new { });
        var artifact = published["artifactId"]!.GetValue<string>();
        var source = published["sourceReferenceId"]!.GetValue<string>();
        var draft = await SubmitAsync("Mixed preview draft Test Run", draftMarker);
        await ExecuteAndTestAsync(workbench);

        await using var restarted = await workbench.CrashAndRestartAsync();
        await NativeHostingScenario.LoginAsync(restarted);
        await ExecuteAndTestAsync(restarted);

        async Task<string> SubmitAsync(string name, string marker)
        {
            var root = NativeHostingScenario.Node("write", writeLine, [NativeHostingScenario.Input("text", marker)]);
            var definition = await NativeHostingScenario.PostAsync(workbench, "/design/workflows/definitions/submit", new
            {
                name,
                state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root }
            });
            return definition["version"]!["id"]!.GetValue<string>();
        }

        async Task ExecuteAndTestAsync(WorkbenchProcess app)
        {
            var execute = await NativeHostingScenario.PostAsync(app, $"/runtime/workflows/executables/{artifact}/execute",
                new { sourceReferenceId = source });
            Assert.False(string.IsNullOrWhiteSpace(execute["workflowExecutionId"]?.GetValue<string>()));
            await WaitForOutputAsync(app, publishedMarker);
            var testRun = await NativeHostingScenario.PostAsync(app, $"/publishing/workflows/{draft}/test-runs", new { });
            Assert.False(string.IsNullOrWhiteSpace(testRun["workflowExecutionId"]?.GetValue<string>()));
            await WaitForOutputAsync(app, draftMarker);
        }

        static Task WaitForOutputAsync(WorkbenchProcess app, string marker) => NativeHostingScenario.WaitForAsync(app,
            () => app.Output.Split('\n').Any(line => line.TrimEnd('\r') == marker),
            $"the local runner to write {marker}", timeout: TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// Each secret the Production overlay requires must fail activation when omitted, so <c>/health/ready</c> reports
    /// the misconfiguration instead of the host reporting ready and failing later: token issuance and authentication,
    /// for the token signing key, or every recovery sweep, for the recovery key. The seed password case also proves the
    /// overlay still names the admin to seed, since only a configured user name makes the missing password an error.
    /// </summary>
    [Theory]
    [InlineData("FoundationIdentityOpenIddict:SigningKey", "No signing key is configured for the OpenIddict identity module")]
    [InlineData("WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence:RecoveryContinuationSigningKey", "Runtime recovery continuation signing key must be configured")]
    [InlineData("FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminPassword", "SeedAdminUserName is configured but SeedAdminPassword is not")]
    public async Task Production_shell_without_a_required_secret_fails_activation(string featureSetting, string expectedError)
    {
        var shell = WorkbenchShell.Production.Without(featureSetting);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var _ = await WorkbenchProcess.StartAsync(shell);
        });

        Assert.Contains("reported the default shell as failed (shell_activation_failed)", failure.Message, StringComparison.Ordinal);
        Assert.True(failure.Message.Contains(expectedError, StringComparison.Ordinal), failure.Message);
    }

    [Fact]
    public async Task Production_actor_default_clears_its_demo_key_when_readiness_names_another_shell()
    {
        const string actorRecoveryKey = "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence:RecoveryContinuationSigningKey";
        var withoutKey = WorkbenchShell.Production.Without(actorRecoveryKey);
        var shell = withoutKey with
        {
            Settings = new Dictionary<string, string>(withoutKey.Settings)
            {
                ["Elsa:Readiness:DefaultShellName"] = "health-probe",
                ["Elsa:Readiness:WarmDefaultShell"] = "false",
                ["CShells:Shells:default:Features:WorkflowsRuntimeKernelHosting:SqlitePath"] = "elsa-workbench-actors.db"
            }
        };
        await using var workbench = await WorkbenchProcess.StartAsync(shell, waitForReady: false);

        // Resolving a route in the real default shell still requires its own production key.
        using var response = await workbench.Client.GetAsync("/modularity/features");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await NativeHostingScenario.WaitForAsync(workbench,
            () => workbench.Output.Contains("Runtime recovery continuation signing key must be configured", StringComparison.Ordinal),
            "the routable default shell to report the missing retained Runtime key", timeout: TimeSpan.FromSeconds(10));
    }

    private static async Task<HashSet<string>> ReadEffectiveFeatureClosureAsync(WorkbenchProcess workbench)
    {
        // CShells reports the effective registered blueprint. The runtime catalog provides the dependencies for it.
        var registered = await workbench.ManagementClient.GetFromJsonAsync<RegisteredShell>("/_admin/shells/default");
        var shell = Assert.IsType<RegisteredShell>(registered);
        Assert.Contains(shell.Generations, generation => generation.State == "Active");

        using var authenticated = new HttpClient { BaseAddress = workbench.Client.BaseAddress };
        using var login = await authenticated.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var catalog = Assert.IsType<DependencyCatalog>(
            await authenticated.GetFromJsonAsync<DependencyCatalog>("/modularity/features"));
        var descriptors = catalog.Features.Where(feature => feature.RuntimeAvailable)
            .ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);

        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(shell.Blueprint.Features);
        while (pending.TryDequeue(out var next))
        {
            if (!closure.Add(next)) continue;
            Assert.True(descriptors.TryGetValue(next, out var descriptor), $"No runtime descriptor for {next}.");
            foreach (var dependency in descriptor.Dependencies.Where(dependency => !dependency.Optional))
                pending.Enqueue(dependency.Id);
        }
        return closure;
    }

    private sealed record RegisteredShell(EffectiveBlueprint Blueprint, ActiveGeneration[] Generations);
    private sealed record EffectiveBlueprint(string[] Features);
    private sealed record ActiveGeneration(string State);
    private sealed record DependencyCatalog(RuntimeFeature[] Features);
    private sealed record RuntimeFeature(string Id, bool RuntimeAvailable, FeatureDependency[] Dependencies);
    private sealed record FeatureDependency(string Id, bool Optional);

    private static void AssertSetting(IReadOnlyDictionary<string, CatalogFeature> catalog, string feature, string setting, string expected)
    {
        Assert.True(
            catalog[feature].Configuration.TryGetValue(setting, out var value),
            $"{feature} is running without a bound {setting} setting.");
        Assert.Equal(expected, value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText());
    }

    private sealed record Health(string Status);
}
