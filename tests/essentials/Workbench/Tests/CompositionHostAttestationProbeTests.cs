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
    private const string ReceiptOverrideKey = "CShells:Shells:default:Features:WorkflowsRuntimeCheckpointPersistence:MaxSegmentCheckpoints";
    // A distinctive valid integer avoids matching unrelated numbers in lifecycle JSON during redaction checks.
    private const string ProcessValue = "73190483";
    private const string Canary = "host-attestation-secret-canary-2039";
    private const string PackageId = "Elsa.AttestationProbe.Loadable";
    private static readonly string[] BundleFiles =
    [
        "shells.json", "shells.Development.json", "shells.Staging.json",
        "appsettings.json", "appsettings.Development.json", "appsettings.Staging.json"
    ];

    [Fact]
    public async Task Fresh_process_uses_one_staged_bundle_for_root_and_shell_configuration()
    {
        const string firstOrigin = "https://first-studio.example.invalid";
        const string secondOrigin = "https://second-studio.example.invalid";
        var stagedBundle = CreateBundle();
        try
        {
            WriteBundle(firstOrigin, 774001);
            await using (var first = await WorkbenchProcess.StartAsync(WorkbenchShell.Development, contentRoot =>
                         {
                             CopyBundle(stagedBundle, contentRoot);
                             // Change the mutable handoff source after the private copy, before the child starts.
                             WriteBundle(secondOrigin, 774002);
                         }))
            {
                Assert.True(await CorsAllowsAsync(first, firstOrigin));
                Assert.False(await CorsAllowsAsync(first, secondOrigin));
                Assert.Contains("774001", await first.ManagementClient.GetStringAsync("/_admin/shells/default/blueprint"), StringComparison.Ordinal);
            }

            await using (var second = await WorkbenchProcess.StartAsync(WorkbenchShell.Development,
                             contentRoot => CopyBundle(stagedBundle, contentRoot)))
            {
                Assert.False(await CorsAllowsAsync(second, firstOrigin));
                Assert.True(await CorsAllowsAsync(second, secondOrigin));
                Assert.Contains("774002", await second.ManagementClient.GetStringAsync("/_admin/shells/default/blueprint"), StringComparison.Ordinal);
                Assert.Equal(BundleFiles.Length, BundleFiles.Count(file => File.Exists(Path.Join(second.ContentRoot, file))));
            }

            // A file-at-a-time deployment has no complete-bundle boundary: source mutation between copies
            // creates a third process with root and shell values from different source revisions.
            WriteBundle(firstOrigin, 774001);
            await using var mixed = await WorkbenchProcess.StartAsync(WorkbenchShell.Development, contentRoot =>
            {
                File.Copy(Path.Join(stagedBundle, "appsettings.Development.json"),
                    Path.Join(contentRoot, "appsettings.Development.json"), overwrite: true);
                WriteBundle(secondOrigin, 774003);
                File.Copy(Path.Join(stagedBundle, "shells.Development.json"),
                    Path.Join(contentRoot, "shells.Development.json"), overwrite: true);
            });
            Assert.True(await CorsAllowsAsync(mixed, firstOrigin));
            Assert.Contains("774003", await mixed.ManagementClient.GetStringAsync("/_admin/shells/default/blueprint"), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(stagedBundle, recursive: true);
        }

        void WriteBundle(string origin, int setting)
        {
            WorkbenchConfigurationFile.WriteValue(
                Path.Join(stagedBundle, "appsettings.Development.json"),
                ["Cors", "AllowedOrigins", "0"], origin);
            WriteSetting(Path.Join(stagedBundle, "shells.Development.json"), setting);
        }
    }

    [Fact]
    public async Task Test_deployer_can_bind_a_complete_retained_copy_to_one_child_without_claiming_candidate_match()
    {
        const string origin = "https://receipt-studio.example.invalid";
        const string overrideValue = "842001";
        var stagedBundle = CreateBundle();
        try
        {
            WorkbenchConfigurationFile.WriteValue(Path.Join(stagedBundle, "appsettings.Development.json"),
                ["Cors", "AllowedOrigins", "0"], origin);
            WriteSetting(Path.Join(stagedBundle, "shells.Development.json"), 841001);
            var shell = WorkbenchShell.Development with
            {
                Settings = new Dictionary<string, string> { [ReceiptOverrideKey] = overrideValue }
            };
            var receipt = TestOwnedArtifactReceipt.Capture(stagedBundle, shell);
            Assert.True(receipt.Matches(receipt.Id, stagedBundle, shell));

            await using var host = await WorkbenchProcess.StartAsync(shell, contentRoot =>
            {
                Assert.True(receipt.Matches(receipt.Id, stagedBundle, shell));
                CopyBundle(stagedBundle, contentRoot);
                Assert.True(receipt.Matches(receipt.Id, contentRoot, shell));
            });
            Assert.True(await CorsAllowsAsync(host, origin));
            var blueprint = await host.ManagementClient.GetStringAsync("/_admin/shells/default/blueprint");
            Assert.Contains(overrideValue, blueprint, StringComparison.Ordinal);
            Assert.DoesNotContain("841001", blueprint, StringComparison.Ordinal);

            var observation = await host.ManagementClient.GetFromJsonAsync<JsonNode>(
                "/_admin/composition/default-shell/observation");
            Assert.NotNull(observation);
            Assert.True((bool?)observation["ready"]);
            Assert.Equal("unverified", (string?)observation["candidateMatch"]);
            var processInstanceId = (string?)observation["processInstanceId"];
            Assert.True(Guid.TryParseExact(processInstanceId, "N", out _));
            // The test launcher knows which receipt it copied before starting this child; the
            // Workbench observer knows the process, but cannot attest the launcher's receipt.
            Assert.DoesNotContain(receipt.Id, observation.ToJsonString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(stagedBundle, recursive: true);
        }
    }

    [Fact]
    public void Test_deployer_refuses_changed_artifact_override_and_arbitrary_receipt_label()
    {
        var stagedBundle = CreateBundle();
        var copiedBundle = Directory.CreateTempSubdirectory("elsa-receipt-copy-").FullName;
        try
        {
            var shell = WorkbenchShell.Development with
            {
                Settings = new Dictionary<string, string> { [ReceiptOverrideKey] = "842001" }
            };
            var receipt = TestOwnedArtifactReceipt.Capture(stagedBundle, shell);
            CopyBundle(stagedBundle, copiedBundle);
            Assert.True(receipt.Matches(receipt.Id, copiedBundle, shell));
            Assert.False(receipt.Matches(Guid.NewGuid().ToString("N"), copiedBundle, shell));
            Assert.False(receipt.Matches(receipt.Id, copiedBundle,
                shell with { Settings = new Dictionary<string, string> { [ReceiptOverrideKey] = "842002" } }));
            Assert.False(receipt.Matches(receipt.Id, copiedBundle,
                shell with { Environment = "Staging" }));

            // A file-at-a-time copy across releases can be complete yet have root and shell
            // bytes from different revisions. The receipt covers the full set, including siblings.
            WorkbenchConfigurationFile.WriteValue(Path.Join(copiedBundle, "appsettings.Development.json"),
                ["Cors", "AllowedOrigins", "0"], "https://other-studio.example.invalid");
            Assert.False(receipt.Matches(receipt.Id, copiedBundle, shell));
            CopyBundle(stagedBundle, copiedBundle);
            WriteSetting(Path.Join(copiedBundle, "shells.Staging.json"), 842003);
            Assert.False(receipt.Matches(receipt.Id, copiedBundle, shell));
            CopyBundle(stagedBundle, copiedBundle);
            WriteSetting(Path.Join(stagedBundle, "shells.Development.json"), 842004);
            Assert.False(receipt.Matches(receipt.Id, stagedBundle, shell));
        }
        finally
        {
            Directory.Delete(stagedBundle, recursive: true);
            Directory.Delete(copiedBundle, recursive: true);
        }
    }

    [Fact]
    public async Task Shell_reload_can_advance_while_startup_bound_host_configuration_stays_old()
    {
        const string oldOrigin = "https://old-studio.example.invalid";
        const string newOrigin = "https://new-studio.example.invalid";
        await using var host = await WorkbenchProcess.StartAsync(WorkbenchShell.Development, directory =>
        {
            WorkbenchConfigurationFile.WriteValue(
                Path.Join(directory, "appsettings.Development.json"),
                ["Cors", "AllowedOrigins", "0"], oldOrigin);
            WriteSetting(Path.Join(directory, "shells.Development.json"), 401);
        });

        Assert.True(await CorsAllowsAsync(host, oldOrigin));
        Assert.False(await CorsAllowsAsync(host, newOrigin));
        var before = await ReadinessAsync(host);

        WorkbenchConfigurationFile.WriteValue(
            Path.Join(host.ContentRoot, "appsettings.Development.json"),
            ["Cors", "AllowedOrigins", "0"], newOrigin);
        WriteSetting(Path.Join(host.ContentRoot, "shells.Development.json"), 402);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        using var response = await host.ManagementClient.PostAsync("/_admin/shells/reload/default", null);
        response.EnsureSuccessStatusCode();
        var reload = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.True((bool?)reload["success"] is true);
        var after = await ReadinessAsync(host);
        Assert.True((int?)after["generation"] > (int?)before["generation"]);

        // CShells reads the changed shell source, while Program.cs's root CORS policy retains its startup value.
        var blueprint = await host.ManagementClient.GetStringAsync("/_admin/shells/default/blueprint");
        Assert.Contains("402", blueprint, StringComparison.Ordinal);
        Assert.True(await CorsAllowsAsync(host, oldOrigin));
        Assert.False(await CorsAllowsAsync(host, newOrigin));
    }

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
        foreach (var file in BundleFiles)
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

    private static async Task<bool> CorsAllowsAsync(WorkbenchProcess host, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", origin);
        using var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed) &&
               allowed.Contains(origin, StringComparer.Ordinal);
    }

    private static void WriteSetting(string path, int value) =>
        WorkbenchConfigurationFile.WriteValue(
            path, ["CShells", "Shells", "default", "Features", FeatureSetting, "MaxSegmentCheckpoints"], value);

    private static string CreateBundle()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-receipt-release-").FullName;
        foreach (var file in BundleFiles)
        {
            var source = WorkbenchBuild.SourceFile(file);
            if (File.Exists(source))
                File.Copy(source, Path.Join(directory, file));
            else
                File.WriteAllText(Path.Join(directory, file), "{}");
        }
        return directory;
    }

    private static void CopyBundle(string source, string destination)
    {
        foreach (var file in BundleFiles)
            File.Copy(Path.Join(source, file), Path.Join(destination, file), overwrite: true);
    }

    /// <summary>
    /// Test-owned receipt. A private byte snapshot is useful for refusing changed fixtures, but Workbench
    /// neither consumes it nor attests that its root/shell configuration was bound to it.
    /// </summary>
    private sealed class TestOwnedArtifactReceipt
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly Dictionary<string, string> _overrides;
        private readonly string _environment;

        private TestOwnedArtifactReceipt(string environment, IReadOnlyDictionary<string, string> overrides,
            Dictionary<string, byte[]> files)
        {
            Id = Guid.NewGuid().ToString("N");
            _environment = environment;
            _overrides = new Dictionary<string, string>(overrides, StringComparer.Ordinal);
            _files = files;
        }

        public string Id { get; }

        public static TestOwnedArtifactReceipt Capture(string directory, WorkbenchShell shell) =>
            new(shell.Environment, shell.Settings, BundleFiles.ToDictionary(
                file => file, file => File.ReadAllBytes(Path.Join(directory, file)), StringComparer.Ordinal));

        public bool Matches(string label, string directory, WorkbenchShell shell) =>
            label == Id && Directory.Exists(directory) && shell.Environment == _environment &&
            BundleFiles.ToHashSet(StringComparer.Ordinal).SetEquals(
                Directory.GetFiles(directory, "*.json").Select(path => Path.GetFileName(path)!)) &&
            shell.Settings.Count == _overrides.Count &&
            shell.Settings.All(setting => _overrides.TryGetValue(setting.Key, out var value) && value == setting.Value) &&
            _files.All(file => File.Exists(Path.Join(directory, file.Key)) &&
                File.ReadAllBytes(Path.Join(directory, file.Key)).AsSpan().SequenceEqual(file.Value));
    }
}
