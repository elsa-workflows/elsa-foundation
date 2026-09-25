using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// Host-side evidence for spec 177 T010. The private CShells blueprint can show effective configuration, but the
/// public readiness and authenticated reload contracts expose only lifecycle/generation data.
/// </summary>
public sealed class CompositionHostAttestationProbeTests
{
    private const string FeatureSetting = "WorkflowsRuntimeCheckpointPersistence";
    private const string ProcessValue = "731";
    private const string Canary = "host-attestation-secret-canary-2039";
    private const string PackageId = "Elsa.AttestationProbe.Loadable";

    [Fact]
    public async Task All_six_bundle_files_and_process_override_have_no_generation_bound_marker()
    {
        var overrideKey = $"CShells:Shells:default:Features:{FeatureSetting}:MaxSegmentCheckpoints";
        var shell = WorkbenchShell.Development with
        {
            Settings = new Dictionary<string, string> { [overrideKey] = ProcessValue }
        };
        await using var host = await WorkbenchProcess.StartAsync(shell, directory =>
        {
            WriteSetting(Path.Combine(directory, "shells.Development.json"), 900101);
            WriteSetting(Path.Combine(directory, "appsettings.Development.json"), 900102);
            WriteSetting(Path.Combine(directory, "shells.Staging.json"), 900103);
            WriteSetting(Path.Combine(directory, "appsettings.Staging.json"), 900104);
        });

        var privateBlueprint = await host.ManagementClient.GetStringAsync("/_admin/shells/default/blueprint");
        Assert.True(privateBlueprint.Contains(ProcessValue, StringComparison.Ordinal));
        Assert.True(!privateBlueprint.Contains("900101", StringComparison.Ordinal));

        var readiness = await ReadinessAsync(host);
        var safeResponses = new List<string>();
        foreach (var file in new[]
                 {
                     "shells.json", "shells.Development.json", "shells.Staging.json",
                     "appsettings.json", "appsettings.Development.json", "appsettings.Staging.json"
                 })
        {
            var path = Path.Combine(host.ContentRoot, file);
            WriteSetting(path, 200 + safeResponses.Count);
            await Task.Delay(TimeSpan.FromMilliseconds(1200)); // Let the JSON provider observe the replacement.

            using var response = await host.ManagementClient.PostAsync("/_admin/shells/reload/default", null);
            Assert.True(response.IsSuccessStatusCode);
            var reload = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
            Assert.True((bool?)reload["success"] is true);
            var nextGeneration = (int?)reload["newShell"]?["generation"];
            Assert.True(nextGeneration > (int)readiness["generation"]!);
            readiness = await ReadinessAsync(host);
            Assert.Equal(nextGeneration, (int?)readiness["generation"]);
            safeResponses.Add(reload.ToJsonString() + readiness.ToJsonString());
        }

        // Invalid PKCS#8 material fails candidate activation. The response remains useful for lifecycle recovery,
        // while the previously active generation remains the one served by readiness.
        var failedGeneration = (int)readiness["generation"]!;
        var failedOverlay = Path.Combine(host.ContentRoot, "shells.Development.json");
        WorkbenchConfigurationFile.WriteOpenIddictSigningKey(failedOverlay, Canary);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        using (var response = await host.ManagementClient.PostAsync("/_admin/shells/reload/default", null))
        {
            Assert.True(response.IsSuccessStatusCode);
            var reload = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
            Assert.True((bool?)reload["success"] is false);
            safeResponses.Add(reload.ToJsonString());
        }

        readiness = await ReadinessAsync(host);
        Assert.Equal(failedGeneration, (int?)readiness["generation"]);
        safeResponses.Add(readiness.ToJsonString());
        Assert.True(safeResponses.All(value => !value.Contains(Canary, StringComparison.Ordinal)));
        Assert.True(safeResponses.All(value => !value.Contains(ProcessValue, StringComparison.Ordinal)));
        Assert.True(safeResponses.All(value => !value.Contains("candidateMatch", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Loadable_local_package_versions_are_recorded_by_separate_host_starts()
    {
        await AssertRecordedPackageVersionAsync("1.0.0");
        await AssertRecordedPackageVersionAsync("2.0.0");
    }

    private static async Task AssertRecordedPackageVersionAsync(string version)
    {
        await using var host = await WorkbenchProcess.StartAsync(WorkbenchShell.Development, directory =>
            WritePackage(directory, version));

        var readiness = await ReadinessAsync(host);
        Assert.Equal("ready", (string?)readiness["status"]);
        Assert.True((int?)readiness["generation"] > 0);
        var safeReadback = readiness.ToJsonString();
        Assert.True(!safeReadback.Contains(PackageId, StringComparison.Ordinal));
        Assert.True(!safeReadback.Contains(version, StringComparison.Ordinal));
        Assert.True(!safeReadback.Contains("candidateMatch", StringComparison.OrdinalIgnoreCase));

        var statePath = Path.Combine(host.ContentRoot, ".nuplane", "store-state.json");
        using var state = JsonDocument.Parse(await File.ReadAllBytesAsync(statePath));
        var descriptor = state.RootElement
            .GetProperty("activePackageDescriptorsById")
            .GetProperty(PackageId);
        Assert.Equal(version, descriptor.GetProperty("version").GetString());
    }

    private static void WritePackage(string contentRoot, string version)
    {
        var assemblyPath = Path.Combine(Path.GetDirectoryName(WorkbenchBuild.AssemblyPath())!, "Elsa.Primitives.dll");
        var packagePath = Path.Combine(contentRoot, "packages", $"{PackageId}.{version}.nupkg");
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        using (var nuspec = new StreamWriter(archive.CreateEntry($"{PackageId}.nuspec").Open(), Encoding.UTF8))
            nuspec.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata><id>{PackageId}</id><version>{version}</version><authors>Elsa</authors><description>Attestation probe fixture</description><dependencies /></metadata>
                </package>
                """);

        archive.CreateEntryFromFile(assemblyPath, $"lib/net10.0/{Path.GetFileName(assemblyPath)}");
    }

    private static async Task<JsonNode> ReadinessAsync(WorkbenchProcess host)
    {
        using var response = await host.Client.GetAsync("/health/ready");
        Assert.True(response.IsSuccessStatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    private static void WriteSetting(string path, int value) =>
        WorkbenchConfigurationFile.WriteValue(
            path, ["CShells", "Shells", "default", "Features", FeatureSetting, "MaxSegmentCheckpoints"], value);
}
