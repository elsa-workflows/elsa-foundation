using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;

namespace Elsa.Rules.Tests;

public sealed class RuleDeploymentIdentitySourceTests
{
    [Fact]
    public async Task Reads_and_snapshots_a_pin_when_loaded_assembly_assets_match_the_receipt_and_active_deps_target()
    {
        using var deployment = new TestDeployment();
        var assets = SemanticAssets();
        deployment.WriteDependencies(DependenciesJson(assets, targetName: "net10.0/linux-x64", includeDecoyTarget: true,
            includeProjectRoot: true));
        deployment.WriteReceipt(ReceiptJson(assets));

        var source = deployment.CreateSource();
        var pin = await source.ReadAsync();

        Assert.NotNull(pin);
        Assert.Equal(3, pin.Packages.Count);
        Assert.Equal(1, pin.Packages.Count(package => package.PackageId == "Elsa.Rules.Core"));
        Assert.Equal(1, pin.Packages.Count(package => package.PackageId == "Elsa.Rules.Runtime"));
        Assert.Equal(1, pin.Packages.Count(package => package.PackageId == "Elsa.Rules.Persistence.Core"));

        deployment.WriteReceipt("malformed after the validated snapshot");
        var cached = await source.ReadAsync();
        Assert.NotNull(cached);
        Assert.Equal(RuleRecordCodec.PinHash(pin), RuleRecordCodec.PinHash(cached));
    }

    [Fact]
    public async Task Missing_or_malformed_receipt_is_unavailable()
    {
        using var deployment = new TestDeployment();
        var assets = SemanticAssets();
        deployment.WriteDependencies(DependenciesJson(assets));
        Assert.Null(await deployment.CreateSource().ReadAsync());

        deployment.WriteReceipt("not JSON");
        Assert.Null(await deployment.CreateSource().ReadAsync());

        deployment.WriteReceipt(ReceiptJson(assets).Replace(
            "\"receiptSchemaVersion\":1,", "\"receiptSchemaVersion\":1,\"receiptSchemaVersion\":1,", StringComparison.Ordinal));
        Assert.Null(await deployment.CreateSource().ReadAsync());
    }

    [Fact]
    public async Task Project_reference_libraries_do_not_create_a_successful_source_build_identity()
    {
        using var deployment = new TestDeployment();
        var assets = SemanticAssets();
        deployment.WriteDependencies(DependenciesJson(assets, libraryType: "project"));
        deployment.WriteReceipt(ReceiptJson(assets));

        Assert.Null(await deployment.CreateSource().ReadAsync());
    }

    [Fact]
    public async Task Every_required_package_must_pin_its_actual_loaded_rules_assembly()
    {
        using var deployment = new TestDeployment();
        var assets = SemanticAssets();
        var core = assets.Single(asset => asset.PackageId == "Elsa.Rules.Core");
        var runtime = assets.Single(asset => asset.PackageId == "Elsa.Rules.Runtime");
        var persistence = assets.Single(asset => asset.PackageId == "Elsa.Rules.Persistence.Core");
        var misattributed = new[]
        {
            core with { PackageRelativeAssetPath = persistence.PackageRelativeAssetPath,
                AssemblyFullName = persistence.AssemblyFullName, AssetSha256 = persistence.AssetSha256 },
            runtime with { PackageRelativeAssetPath = core.PackageRelativeAssetPath,
                AssemblyFullName = core.AssemblyFullName, AssetSha256 = core.AssetSha256 },
            persistence with { PackageRelativeAssetPath = runtime.PackageRelativeAssetPath,
                AssemblyFullName = runtime.AssemblyFullName, AssetSha256 = runtime.AssetSha256 }
        };
        deployment.WriteDependencies(DependenciesJson(misattributed));
        deployment.WriteReceipt(ReceiptJson(misattributed));

        Assert.Null(await deployment.CreateSource().ReadAsync());
    }

    [Fact]
    public async Task Relative_deployment_paths_are_unavailable_instead_of_using_the_working_directory()
    {
        using var deployment = new TestDeployment();
        var assets = SemanticAssets();
        deployment.WriteDependencies(DependenciesJson(assets));
        deployment.WriteReceipt(ReceiptJson(assets));

        Assert.Null(await new RuleDeploymentIdentitySource("host.deps.json", deployment.ReceiptPath,
            deployment.HostRoot).ReadAsync());
        Assert.Null(await new RuleDeploymentIdentitySource(deployment.DepsPath, deployment.ReceiptPath,
            "").ReadAsync());
        var volumeRoot = Path.GetPathRoot(deployment.HostRoot)!;
        Assert.Null(await new RuleDeploymentIdentitySource(deployment.DepsPath, deployment.ReceiptPath,
            volumeRoot).ReadAsync());
    }

    [Fact]
    public async Task Package_version_and_nuget_hash_must_match_the_active_dependency_graph()
    {
        using var deployment = new TestDeployment();
        var assets = SemanticAssets();
        deployment.WriteDependencies(DependenciesJson(assets, versionOverride: "9.9.9"));
        deployment.WriteReceipt(ReceiptJson(assets));
        Assert.Null(await deployment.CreateSource().ReadAsync());

        deployment.WriteDependencies(DependenciesJson(assets, dependencyHashOverride: ContentHash("wrong")));
        Assert.Null(await deployment.CreateSource().ReadAsync());
    }

    [Fact]
    public async Task Selected_asset_path_assembly_identity_and_bytes_must_match_the_loaded_file()
    {
        using var deployment = new TestDeployment();
        var assets = SemanticAssets();
        deployment.WriteDependencies(DependenciesJson(assets));

        var wrongHash = assets.Select(asset => asset with { AssetSha256 = new string('0', 64) }).ToArray();
        deployment.WriteReceipt(ReceiptJson(wrongHash));
        Assert.Null(await deployment.CreateSource().ReadAsync());

        var wrongPath = assets.Select(asset => asset with { PackageRelativeAssetPath = "lib/net10.0/not-loaded.dll" }).ToArray();
        deployment.WriteReceipt(ReceiptJson(wrongPath));
        Assert.Null(await deployment.CreateSource().ReadAsync());

        var wrongName = assets.Select(asset => asset with { AssemblyFullName = "Unknown.Assembly, Version=1.0.0.0" }).ToArray();
        deployment.WriteReceipt(ReceiptJson(wrongName));
        Assert.Null(await deployment.CreateSource().ReadAsync());
    }

    [Fact]
    public async Task Unsafe_package_paths_and_unreadable_paths_outside_the_host_root_are_unavailable()
    {
        using var deployment = new TestDeployment();
        var assets = SemanticAssets();
        deployment.WriteDependencies(DependenciesJson(assets));
        var unsafePath = assets.Select(asset => asset with { PackageRelativeAssetPath = "lib/../Elsa.Rules.Core.dll" }).ToArray();
        deployment.WriteReceipt(ReceiptJson(unsafePath));
        Assert.Null(await deployment.CreateSource().ReadAsync());

        deployment.WriteReceipt(ReceiptJson(assets));
        using var outside = new TestDeployment(root: Path.GetTempPath());
        File.Copy(deployment.DepsPath, outside.DepsPath, overwrite: true);
        File.Copy(deployment.ReceiptPath, outside.ReceiptPath, overwrite: true);
        var outsideSource = new RuleDeploymentIdentitySource(outside.DepsPath, outside.ReceiptPath, AppContext.BaseDirectory);
        Assert.Null(await outsideSource.ReadAsync());
    }

    [Fact]
    public async Task Cancellation_is_propagated_and_does_not_poison_the_cached_snapshot()
    {
        using var deployment = new TestDeployment();
        var assets = SemanticAssets();
        deployment.WriteDependencies(DependenciesJson(assets));
        deployment.WriteReceipt(ReceiptJson(assets));
        var source = deployment.CreateSource();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await source.ReadAsync(cancellation.Token));

        Assert.NotNull(await source.ReadAsync());
    }

    private static AssetDescriptor[] SemanticAssets()
    {
        var assemblies = new (string PackageId, Assembly Assembly)[]
        {
            ("Elsa.Rules.Core", typeof(RuleProgram).Assembly),
            ("Elsa.Rules.Runtime", typeof(RuleDeploymentIdentitySource).Assembly),
            ("Elsa.Rules.Persistence.Core", typeof(RuleRecordCodec).Assembly)
        };

        return assemblies.Select(item =>
        {
            var location = item.Assembly.Location;
            var name = item.Assembly.GetName().FullName!;
            return new AssetDescriptor(item.PackageId, "4.0.0", ContentHash(item.PackageId),
                $"lib/net10.0/{Path.GetFileName(location)}", name,
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(location))));
        }).ToArray();
    }

    private static string DependenciesJson(IReadOnlyList<AssetDescriptor> assets, string libraryType = "package",
        string? targetName = null, bool includeDecoyTarget = false, string? versionOverride = null,
        string? dependencyHashOverride = null, bool includeProjectRoot = false)
    {
        targetName ??= "net10.0";
        var targetLibraries = new Dictionary<string, object>();
        var libraries = new Dictionary<string, object>();
        foreach (var group in assets.GroupBy(asset => asset.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var version = versionOverride ?? first.Version;
            var libraryKey = $"{first.PackageId}/{version}";
            var runtime = group.ToDictionary(asset => asset.PackageRelativeAssetPath,
                _ => (object)new Dictionary<string, object>());
            targetLibraries[libraryKey] = new { runtime };

            var metadata = new Dictionary<string, object> { ["type"] = libraryType };
            if (dependencyHashOverride is not null)
                metadata["sha512"] = "sha512-" + dependencyHashOverride;
            else
                metadata["sha512"] = "sha512-" + first.NuGetContentHash;
            libraries[libraryKey] = metadata;
        }

        if (includeProjectRoot)
        {
            const string projectKey = "HostApplication/1.0.0";
            targetLibraries[projectKey] = new
            {
                runtime = new Dictionary<string, object> { ["lib/net10.0/_._"] = new Dictionary<string, object>() }
            };
            libraries[projectKey] = new Dictionary<string, object> { ["type"] = "project", ["sha512"] = "" };
        }

        var targets = new Dictionary<string, object> { [targetName] = targetLibraries };
        if (includeDecoyTarget)
        {
            var decoy = new Dictionary<string, object>
            {
                ["Elsa.Rules.Core/0.0.1"] = new { runtime = new Dictionary<string, object>() }
            };
            targets["net10.0"] = decoy;
        }

        return JsonSerializer.Serialize(new
        {
            runtimeTarget = new { name = targetName },
            targets,
            libraries
        });
    }

    private static string ReceiptJson(IReadOnlyList<AssetDescriptor> assets)
    {
        var packages = assets.GroupBy(asset => asset.PackageId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                id = group.Key,
                version = group.First().Version,
                contentHash = group.First().NuGetContentHash,
                assets = group.OrderBy(asset => asset.PackageRelativeAssetPath, StringComparer.Ordinal)
                    .Select(asset => new
                    {
                        path = asset.PackageRelativeAssetPath,
                        assembly = asset.AssemblyFullName,
                        sha256 = asset.AssetSha256
                    }).ToArray()
            }).ToArray();

        return JsonSerializer.Serialize(new
        {
            receiptSchemaVersion = 1,
            pin = new
            {
                pinSchemaVersion = 1,
                packages,
                languageId = RulesCodec.LanguageId,
                languageVersion = 1,
                receiptSchemaVersion = 1,
                stateSchemaVersion = 1
            }
        });
    }

    private static string ContentHash(string value) =>
        Convert.ToBase64String(SHA512.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record AssetDescriptor(string PackageId, string Version, string NuGetContentHash,
        string PackageRelativeAssetPath, string AssemblyFullName, string AssetSha256);

    private sealed class TestDeployment : IDisposable
    {
        public TestDeployment(string? root = null)
        {
            HostRoot = root ?? AppContext.BaseDirectory;
            DirectoryPath = Path.Combine(HostRoot, $".rules-identity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            DepsPath = Path.Combine(DirectoryPath, "host.deps.json");
            ReceiptPath = Path.Combine(DirectoryPath, RuleDeploymentIdentitySource.DefaultReceiptFileName);
        }

        public string HostRoot { get; }
        public string DirectoryPath { get; }
        public string DepsPath { get; }
        public string ReceiptPath { get; }

        public RuleDeploymentIdentitySource CreateSource() =>
            new(DepsPath, ReceiptPath, HostRoot);

        public void WriteDependencies(string dependencies) => File.WriteAllText(DepsPath, dependencies);

        public void WriteReceipt(string receipt) => File.WriteAllText(ReceiptPath, receipt);

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
