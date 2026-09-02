using System.Diagnostics;
using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class BenchmarkCompositionFingerprintTests
{
    [Fact]
    public void Groundwork_descriptor_is_deterministic_and_order_independent()
    {
        var first = BenchmarkCompositionFingerprint.Describe(Request());
        var currentPackages = ProviderPackageProvenance.CurrentVersions(
            SourceProvenance.FindRepositoryRoot(), "groundwork-v2", "sqlite");
        var second = BenchmarkCompositionFingerprint.Describe(Request(
            providerConfiguration: new Dictionary<string, string>
            {
                ["topology"] = "file-backed",
                ["provider-mode"] = "local"
            },
            packageVersions: currentPackages.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value)));

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Matches("^[0-9a-f]{64}$", first.Fingerprint);
    }

    [Fact]
    public void Fingerprint_changes_when_each_identity_component_changes()
    {
        var descriptor = BenchmarkCompositionFingerprint.Describe(Request());
        var variants = new[]
        {
            descriptor with { FormatVersion = 2 },
            descriptor with { IsGroundwork = false },
            descriptor with { WorkloadId = "alternate-workload" },
            descriptor with { WorkloadVersion = "1.1.1" },
            descriptor with { Adapter = "groundwork-v2-alternate" },
            descriptor with { PhysicalForm = "alternate-form" },
            descriptor with { Provider = "postgresql" },
            descriptor with { ProviderVersion = "different-provider" },
            descriptor with { ProviderTopology = "different-topology" },
            descriptor with { ProviderConfiguration = [new("provider-mode", "remote")] },
            descriptor with { PackageVersions = [new("Groundwork.Sqlite", "0.4.0-preview.9")] },
            descriptor with { Features = [descriptor.Features[0] with { Id = "groundwork-runtime-alternate" }] },
            descriptor with { Features = [descriptor.Features[0] with { SchemaIdentity = "changed-schema" }] },
            descriptor with { Features = [descriptor.Features[0] with { StorageUnitIds = ["different-unit"] }] },
            descriptor with { StorageUnits = [descriptor.StorageUnits[0] with { Target = "alternate-target" }] },
            descriptor with { StorageUnits = [descriptor.StorageUnits[0] with { UnitId = "alternate-unit" }] },
            descriptor with { StorageUnits = [descriptor.StorageUnits[0] with { Name = "Alternate unit" }] },
            descriptor with { StorageUnits = [descriptor.StorageUnits[0] with { SchemaVersion = 2 }] },
            descriptor with { StorageUnits = [descriptor.StorageUnits[0] with { Fingerprint = new string('f', 64) }] }
        };

        Assert.All(variants, variant => Assert.NotEqual(descriptor.Fingerprint, variant.Fingerprint));
    }

    [Fact]
    public void Registry_and_serialized_command_document_share_one_digest()
    {
        var request = Request();
        var descriptor = BenchmarkCompositionFingerprint.Describe(request);
        var document = BenchmarkCompositionFingerprint.DescribeDocument(request);
        var descriptorJson = JsonSerializer.Serialize(descriptor, ArtifactStore.JsonOptions);
        var json = JsonSerializer.Serialize(document, ArtifactStore.JsonOptions);
        using var descriptorDocument = JsonDocument.Parse(descriptorJson);
        using var parsed = JsonDocument.Parse(json);

        Assert.False(descriptorDocument.RootElement.TryGetProperty("Fingerprint", out _));
        Assert.Equal(descriptor.Fingerprint, document.Fingerprint);
        Assert.Equal(descriptor.Fingerprint, parsed.RootElement.GetProperty("Fingerprint").GetString());
        Assert.Equal(descriptor.StorageUnits.Select(unit => unit.UnitId),
            document.Descriptor.StorageUnits.Select(unit => unit.UnitId));
    }

    [Fact]
    public void Ef_descriptor_is_explicit_and_distinct_from_groundwork()
    {
        var groundwork = BenchmarkCompositionFingerprint.Describe(Request());
        var ef = BenchmarkCompositionFingerprint.Describe(Request(
            workload: "secret-create-read-list",
            workloadVersion: "1.1.0",
            adapter: "ef-secret-repository",
            physicalForm: "entity-type-specific-physical-tables"));

        Assert.NotEqual(groundwork.Fingerprint, ef.Fingerprint);
        Assert.Contains(ef.Features, feature => feature.Id == "ef-secret-repository");
        Assert.Empty(ef.StorageUnits);
    }

    [Fact]
    public void Diagnostics_descriptor_covers_the_direct_diagnostics_registry_units()
    {
        var descriptor = BenchmarkCompositionFingerprint.Describe(Request(
            workload: "diagnostics-durable-history",
            workloadVersion: "1.2.0",
            physicalForm: "ordinary-groundwork-diagnostics-units"));
        var diagnostics = Assert.Single(descriptor.Features, feature => feature.Id == "groundwork-diagnostics");

        Assert.Equal(9, diagnostics.StorageUnitIds.Count);
        Assert.All(diagnostics.StorageUnitIds, unitId =>
            Assert.Contains(descriptor.StorageUnits, unit => unit.UnitId == unitId));
    }

    [Fact]
    public void Unsupported_and_incomplete_requests_fail_closed()
    {
        Assert.Throws<PerformanceContractException>(() =>
            BenchmarkCompositionFingerprint.Describe(Request(adapter: "unknown-adapter")));
        Assert.Throws<PerformanceContractException>(() =>
            BenchmarkCompositionFingerprint.Describe(Request(providerConfiguration: new Dictionary<string, string>())));
        Assert.Throws<PerformanceContractException>(() =>
            BenchmarkCompositionFingerprint.Describe(Request(packageVersions: new Dictionary<string, string>())));
        Assert.Throws<PerformanceContractException>(() =>
            BenchmarkCompositionFingerprint.Describe(Request(providerConfiguration: new Dictionary<string, string>
            {
                ["connection-string"] = "not-a-connection"
            })));
    }

    [Fact]
    public void Registry_units_cannot_be_added_without_feature_classification()
    {
        var request = Request();
        var descriptor = BenchmarkCompositionFingerprint.Describe(request);
        var extra = new BenchmarkCompositionFingerprint.BenchmarkStorageUnitDescriptor(
            "default", "unclassified-unit", "Unclassified", 1, new string('1', 64));

        Assert.Throws<PerformanceContractException>(() =>
            BenchmarkCompositionFingerprint.ValidateRegistryCoverage(
                BenchmarkCompositionFingerprint.CompositionSelection.For(request),
                descriptor.StorageUnits.Append(extra).ToArray(),
                descriptor.Features));
    }

    [Fact]
    public void Feature_units_cannot_be_missing_from_the_registry()
    {
        var request = Request();
        var descriptor = BenchmarkCompositionFingerprint.Describe(request);
        var declaredUnit = descriptor.Features.SelectMany(feature => feature.StorageUnitIds).First();

        Assert.Throws<PerformanceContractException>(() =>
            BenchmarkCompositionFingerprint.ValidateRegistryCoverage(
                BenchmarkCompositionFingerprint.CompositionSelection.For(request),
                descriptor.StorageUnits.Where(unit => unit.UnitId != declaredUnit).ToArray(),
                descriptor.Features));
    }

    [Fact]
    public void Registry_path_composes_the_same_optional_families_as_a_live_groundwork_host()
    {
        var runtimeSelection = new BenchmarkCompositionFingerprint.CompositionSelection(
            true, IncludeDistributed: false, IncludeIdentity: false, IncludeSecrets: false,
            IncludeDiagnostics: false, Features: []);
        var allSelection = new BenchmarkCompositionFingerprint.CompositionSelection(
            true, IncludeDistributed: true, IncludeIdentity: true, IncludeSecrets: true,
            IncludeDiagnostics: true, Features: []);

        var runtime = RuntimeStoreComposition.CreateRegistry(runtimeSelection);
        var all = RuntimeStoreComposition.CreateRegistry(allSelection);

        Assert.NotEmpty(runtime.Registrations);
        Assert.True(all.Registrations.Count > runtime.Registrations.Count);
    }

    [Fact]
    public void Request_composition_mismatch_is_rejected()
    {
        var request = Request() with { CompositionFingerprint = new string('0', 64) };
        Assert.Throws<PerformanceContractException>(() => BenchmarkCompositionFingerprint.Validate(request));
    }

    [Fact]
    public void Wrong_package_version_is_rejected_before_digest_generation()
    {
        var current = ProviderPackageProvenance.CurrentVersions(
            SourceProvenance.FindRepositoryRoot(), "groundwork-v2", "sqlite");
        var wrong = current.ToDictionary(pair => pair.Key, pair => pair.Value + ".wrong");

        var exception = Assert.Throws<PerformanceContractException>(() =>
            BenchmarkCompositionFingerprint.Describe(Request(packageVersions: wrong)));

        Assert.Contains("must exactly match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_composition_command_has_a_real_process_contract()
    {
        var hostAssembly = typeof(BenchmarkCompositionFingerprint).Assembly.Location;
        Assert.True(File.Exists(hostAssembly), $"AdapterHost assembly was not built: {hostAssembly}");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = SourceProvenance.FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(hostAssembly);
        start.ArgumentList.Add("describe-composition");
        start.ArgumentList.Add("--request");
        start.ArgumentList.Add("{}");

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(2, process.ExitCode);
        Assert.Empty(stdout);
        Assert.Contains("request JSON is invalid", stderr, StringComparison.Ordinal);
    }

    private static RunRequest Request(
        string workload = "checkpoint-commit",
        string workloadVersion = "1.1.0",
        string adapter = "groundwork-v2",
        string physicalForm = "checkpoint-unit-of-work-with-linked-outbox",
        string provider = "sqlite",
        IReadOnlyDictionary<string, string>? providerConfiguration = null,
        IReadOnlyDictionary<string, string>? packageVersions = null)
    {
        var definition = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[workload];
        var topology = definition.RequiredProviderEvidence[provider];
        return new(
            "test-cohort",
            "test-measurement",
            workload,
            definition.Version == workloadVersion ? definition.Version : workloadVersion,
            provider,
            adapter,
            physicalForm,
            "medium",
            new string('a', 40),
            new string('b', 64),
            packageVersions ?? ProviderPackageProvenance.CurrentVersions(
                SourceProvenance.FindRepositoryRoot(), adapter, provider),
            new string('c', 64),
            new string('d', 64),
            "3.46.0",
            topology,
            providerConfiguration ?? new Dictionary<string, string>
            {
                ["provider-mode"] = "local",
                ["topology"] = "file-backed"
            },
            definition.Input.Seed,
            definition.Input.FingerprintSha256,
            "checkpoint-native-plan",
            "checkpoint.sqlite.native-plan.json",
            new string('f', 64),
            ProcessKind.Warmup,
            0);
    }
}
