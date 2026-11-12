using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using CShells.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Loading;
using Nuplane.Loading.Hosting.Builder;

namespace Elsa.Studio.Tests.Administration;

// TEMPORARY diagnostic for #4090. Removed before merge.
public sealed class PackageMaintenanceRaceDiagnosticTests
{
    private const string PruneUrl = "/_elsa/module-management/prune";

    [Fact]
    public async Task Diagnose_graph_use_transitions_after_retained_generation_replacement()
    {
        var report = new StringBuilder();
        string root;
        await using (var installer = await AdministrationHost.StartAsync(nativeStore: true, keepFiles: true))
        {
            root = installer.Root;
            PackageAssetTests.Pack(installer, "0.5.0", "obsolete");
            await PackageAssetTests.Reconcile(installer);
        }
        File.Delete(Path.Combine(root, "feed", PackageAssetTests.PackageId + ".0.5.0.nupkg"));
        await using var host = await AdministrationHost.StartAsync(root: root, nativeStore: true, loadPackages: true,
            configureNuplane: nuplane => nuplane.AutoloadPackages(loading => loading.WithDefaultLoadMode(PackageLoadMode.Collectible)
                .WithDeactivationTimeout(TimeSpan.FromMilliseconds(100))),
            beforeStudio: builder => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:Authoring:Features:StudioReuseProbe"] = "true"
            }));
        await Snapshot(host, report, "host2-started");
        PackageAssetTests.Pack(host, "1.0.0", "first", assembly: "First");
        await PackageAssetTests.Reconcile(host);
        await Snapshot(host, report, "after-reconcile-1.0.0");
        var registry = host.App.Services.GetRequiredService<IShellRegistry>();
        var first = await registry.GetOrActivateAsync("Authoring");
        await using var retained = first.BeginScope();
        Assert.Equal("first", retained.ServiceProvider.GetRequiredService<string>());
        await Snapshot(host, report, "after-activate-gen1");
        await Settle(host, report, "settle-gen1");
        using (var policy = await host.Client.PutAsJsonAsync("/_elsa/module-management/retention-policy", new { keepNewestInactiveVersionsPerPackage = 0 }))
            policy.EnsureSuccessStatusCode();
        var preview = await Prune(host, new JsonObject());
        report.AppendLine("first-prune: " + Summary(await Prune(host, Execute(preview))));

        File.Delete(Path.Combine(root, "feed", PackageAssetTests.PackageId + ".1.0.0.nupkg"));
        PackageAssetTests.Pack(host, "2.0.0", "second", assembly: "Second");
        await PackageAssetTests.Reconcile(host);
        await Snapshot(host, report, "after-reconcile-2.0.0");
        await Settle(host, report, "settle-gen2");
        var replacedPreview = await Prune(host, new JsonObject());
        report.AppendLine("replaced-prune: " + Summary(await Prune(host, Execute(replacedPreview))));
        await Snapshot(host, report, "after-replaced-prune");
        Assert.Equal("first", retained.ServiceProvider.GetRequiredService<string>());
        Assert.Fail("DIAGNOSTIC #4090\n" + report);
    }

    private static async Task Settle(AdministrationHost host, StringBuilder report, string label)
    {
        for (var round = 0; round < 4; round++)
        {
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            await Task.Delay(1500);
            await Snapshot(host, report, $"{label}-{round}");
        }
    }

    private static async Task Snapshot(AdministrationHost host, StringBuilder report, string label)
        => report.AppendLine($"{label}: {Summary(await Prune(host, new JsonObject()))}");

    private static string Summary(JsonObject result)
    {
        var uses = (result["uses"] as JsonArray ?? new JsonArray())
            .Select(use => $"{(string?)use!["ownershipState"]}/{(string?)use["snapshotState"]}[{string.Join(",", Versions(use["graph"]).Distinct().Order())}]")
            .Order();
        var diagnostics = (result["diagnostics"] as JsonArray ?? new JsonArray())
            .Select(diagnostic => $"{(string?)diagnostic!["code"]}:{SafeMessage((string?)diagnostic["message"])}");
        var candidates = (result["candidates"] as JsonArray ?? new JsonArray())
            .Select(candidate => $"{(string?)candidate!["install"]?["version"]}={(string?)candidate["classification"]}/{(string?)candidate["physicalOutcome"]}/{string.Join("+", (candidate["reasons"] as JsonArray ?? new JsonArray()).Select(reason => (string?)reason))}");
        return $"status={(string?)result["status"]}; knowledge={(string?)result["protectionKnowledge"]}; inventory={(bool?)result["inventoryComplete"]}; uses=[{string.Join(" ", uses)}]; candidates=[{string.Join(" ", candidates)}]; diagnostics=[{string.Join(" | ", diagnostics)}]";
    }

    private static string SafeMessage(string? message)
        => message is null ? "" : message.Contains('/') || message.Contains('\\') || message.Length > 240 ? "<redacted>" : message;

    private static IEnumerable<string> Versions(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject item:
                foreach (var (key, value) in item)
                {
                    if (string.Equals(key, "version", StringComparison.OrdinalIgnoreCase) && value is JsonValue text && text.TryGetValue<string>(out var version))
                        yield return version;
                    foreach (var nested in Versions(value))
                        yield return nested;
                }
                break;
            case JsonArray array:
                foreach (var nested in array.SelectMany(Versions))
                    yield return nested;
                break;
        }
    }

    private static async Task<JsonObject> Prune(AdministrationHost host, JsonObject request)
    {
        using var response = await host.Client.PostAsJsonAsync(PruneUrl, request);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    private static JsonObject Execute(JsonObject preview) => new()
    {
        ["dryRun"] = false,
        ["rootLabel"] = preview["rootLabel"]!.DeepClone(),
        ["confirmation"] = new JsonObject
        {
            ["rootLabel"] = preview["rootLabel"]!.DeepClone(),
            ["rootIdentity"] = preview["rootIdentity"]!.DeepClone(),
            ["enrollmentEpoch"] = preview["enrollmentEpoch"]!.DeepClone(),
            ["retention"] = preview["retention"]?.DeepClone()
        }
    };
}
