using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workbench.Tests;

/// <summary>Runs the proposed Authoring planning fixture through the rebuilt Workbench host.</summary>
public sealed class AuthoringFixtureHostEvidenceTests
{
    private static readonly string[] HostSupportFeatures = ["FoundationIdentityAbstractions", "FoundationIdentityOidc"];
    private readonly ITestOutputHelper output;

    public AuthoringFixtureHostEvidenceTests(ITestOutputHelper output) => this.output = output;

    private static readonly string[] RequestedFeatures =
    [
        "Primitives", "Serialization", "Mediator", "Events", "Expressions",
        "ApiCapabilities", "ActivitiesDesignApi", "ActivitiesDesignEntityFrameworkCore",
        "ActivitiesDesignReconciliation", "ClrActivityReconciliation", "WorkflowDesignValidations",
        "WorkflowsDesignApi", "WorkflowsDesignEntityFrameworkCore", "WorkflowsPublishing",
        "WorkflowsPublishingApi", "WorkflowsPublishingEntityFrameworkCore"
    ];

    [Fact]
    public async Task Authoring_selection_with_explicit_host_authentication_substrate_activates()
    {
        var shell = WorkbenchShell.Development with
        {
            Settings = new Dictionary<string, string>
            {
                ["Elsa:Persistence:DefaultResource"] = "primary",
                ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
                ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Authoring"
            }
        };
        await using var workbench = await WorkbenchProcess.StartAsync(shell, directory =>
        {
            var shellFile = Path.Combine(directory, "shells.json");
            var root = JsonNode.Parse(File.ReadAllText(shellFile))!;
            var selected = new JsonObject();
            foreach (var feature in RequestedFeatures)
                selected[feature] = new JsonObject();
            foreach (var feature in HostSupportFeatures)
                selected[feature] = new JsonObject();
            root["CShells"]!["Shells"]!["default"]!["Features"] = selected;
            File.WriteAllText(shellFile, root.ToJsonString());

            var appsettingsFile = Path.Combine(directory, "appsettings.json");
            var appsettings = JsonNode.Parse(File.ReadAllText(appsettingsFile))!;
            appsettings["ConnectionStrings"]!["Authoring"] = $"Data Source={Path.Combine(directory, "authoring.db")};Pooling=False";
            File.WriteAllText(appsettingsFile, appsettings.ToJsonString());
        });

        var catalog = await workbench.ReadFeatureCatalogAsync();
        var reportedRunning = catalog.Where(item => item.Runs).Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(RequestedFeatures.Concat(HostSupportFeatures).Order(StringComparer.Ordinal), reportedRunning);
        output.WriteLine("Authored and reported running: " + string.Join(", ", reportedRunning));
        Assert.DoesNotContain(catalog, item => item.Id == "WorkflowsRuntimeApi" && item.Runs);
        Assert.DoesNotContain(catalog, item => item.Id == "WorkflowsRuntimeTriggers" && item.Runs);

        foreach (var route in new[] { "/design/activities/catalog", "/design/workflows/definitions", "/runtime/workflows/executables" })
        {
            using var routeResponse = await workbench.Client.GetAsync(route);
            output.WriteLine($"GET {route} -> {(int)routeResponse.StatusCode}");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, routeResponse.StatusCode);
        }
        using var publishingResponse = await workbench.Client.GetAsync("/publishing/workflows/version-1/publish");
        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, publishingResponse.StatusCode);
        using var absentResponse = await workbench.Client.GetAsync("/not-an-authoring-route");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, absentResponse.StatusCode);
        Assert.True(File.Exists(Path.Combine(workbench.ContentRoot, "authoring.db")), "The selected SQLite resource did not create its database.");
    }
}
