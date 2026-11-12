using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using CShells.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Admin;
using Nuplane.Loading;
using Nuplane.Loading.Hosting.Builder;

namespace Elsa.Studio.Tests.Administration;

// TEMPORARY diagnostic for #4090. Removed before merge.
public sealed class PackageMaintenanceRaceDiagnosticTests
{
    private const string PruneUrl = "/_elsa/module-management/prune";

    public static TheoryData<int> Iterations()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < 40; i++) data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(Iterations))]
    public async Task Replay_exact_retained_generation_sequence(int iteration)
    {
        var report = new StringBuilder($"iteration={iteration}\n");
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
        PackageAssetTests.Pack(host, "1.0.0", "first", assembly: "First");
        await PackageAssetTests.Reconcile(host);
        var registry = host.App.Services.GetRequiredService<IShellRegistry>();
        var first = await registry.GetOrActivateAsync("Authoring");
        await using var retained = first.BeginScope();
        Assert.Equal("first", retained.ServiceProvider.GetRequiredService<string>());
        var keepAllPreview = await Prune(host, new JsonObject());
        var keepAll = await Prune(host, Execute(keepAllPreview));
        report.AppendLine("keep-all: " + Summary(keepAll));
        using (var policy = await host.Client.PutAsJsonAsync("/_elsa/module-management/retention-policy", new { keepNewestInactiveVersionsPerPackage = 0 }))
            policy.EnsureSuccessStatusCode();
        var preview = await Prune(host, new JsonObject());
        var firstPrune = await Prune(host, Execute(preview));
        report.AppendLine("first-prune: " + Summary(firstPrune));
        var admin = host.App.Services.GetRequiredService<INuplaneAdminOperations>();
        _ = Assert.Single((await admin.GetPackagesAsync(CancellationToken.None)).Packages).InstallPath;
        File.Delete(Path.Combine(root, "feed", PackageAssetTests.PackageId + ".1.0.0.nupkg"));
        PackageAssetTests.Pack(host, "2.0.0", "second", assembly: "Second");
        await PackageAssetTests.Reconcile(host);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await using (var current = registry.GetActive("Authoring")!.BeginScope())
            Assert.Equal("second", current.ServiceProvider.GetRequiredService<string>());
        var replacedPreview = await Prune(host, new JsonObject());
        report.AppendLine($"replaced-preview@{clock.ElapsedMilliseconds}ms: " + Summary(replacedPreview));
        var replaced = await Prune(host, Execute(replacedPreview));
        report.AppendLine($"replaced-execute@{clock.ElapsedMilliseconds}ms: " + Summary(replaced));
        if ((string?)replaced["status"] != "Completed")
        {
            report.AppendLine($"after-refusal-preview@{clock.ElapsedMilliseconds}ms: " + Summary(await Prune(host, new JsonObject())));
            var again = await Prune(host, Execute(replacedPreview));
            report.AppendLine($"re-execute@{clock.ElapsedMilliseconds}ms: " + Summary(again));
            await Task.Delay(3000);
            report.AppendLine($"late-preview@{clock.ElapsedMilliseconds}ms: " + Summary(await Prune(host, new JsonObject())));
            Assert.Fail("DIAGNOSTIC #4090 REFUSAL\n" + report);
        }
        Assert.Equal("first", retained.ServiceProvider.GetRequiredService<string>());
    }

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
