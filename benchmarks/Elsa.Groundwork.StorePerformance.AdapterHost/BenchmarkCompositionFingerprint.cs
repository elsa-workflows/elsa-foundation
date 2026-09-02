using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Groundwork.Kernel;
using Groundwork.Kernel.Schema;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Computes the stable composition identity carried by #646 evidence.
/// This is deliberately a description of the selected public adapter composition, not a hash of an
/// assembly, repository tree, generated artifact, or provider connection string.
/// </summary>
internal static class BenchmarkCompositionFingerprint
{
    internal const int FormatVersion = 1;

    public static BenchmarkCompositionDescriptor Describe(RunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestIdentity(request);
        var selection = CompositionSelection.For(request);
        var registry = selection.IsGroundwork
            ? RuntimeStoreComposition.CreateRegistry(selection)
            : null;
        var units = registry?.Registrations
            .Select(registration => new BenchmarkStorageUnitDescriptor(
                registration.TargetName,
                registration.Unit.Id.Value,
                registration.Unit.Name,
                registration.Unit.SchemaVersion,
                registration.Fingerprint))
            .OrderBy(unit => unit.Target, StringComparer.Ordinal)
            .ThenBy(unit => unit.UnitId, StringComparer.Ordinal)
            .ToArray() ?? [];

        var features = selection.Features
            .Select(feature => feature with
            {
                StorageUnitIds = feature.StorageUnitIds
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(feature => feature.Id, StringComparer.Ordinal)
            .ToArray();

        ValidateRegistryCoverage(selection, units, features);

        var descriptor = new BenchmarkCompositionDescriptor(
            FormatVersion,
            selection.IsGroundwork,
            request.WorkloadId,
            request.WorkloadVersion,
            request.Adapter,
            request.PhysicalForm,
            request.Provider,
            request.ProviderVersion,
            request.ProviderTopology,
            request.ProviderConfiguration
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new BenchmarkNameValue(pair.Key, pair.Value))
                .ToArray(),
            request.PackageVersions
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new BenchmarkNameValue(pair.Key, pair.Value))
                .ToArray(),
            features,
            units);
        descriptor.Validate();
        return descriptor;
    }

    internal static BenchmarkCompositionDocument DescribeDocument(RunRequest request)
    {
        var descriptor = Describe(request);
        return new BenchmarkCompositionDocument(descriptor.FormatVersion, descriptor.Fingerprint, descriptor);
    }

    internal static void ValidateRegistryCoverage(
        CompositionSelection selection,
        IReadOnlyList<BenchmarkStorageUnitDescriptor> units,
        IReadOnlyList<BenchmarkFeatureDescriptor> features)
    {
        if (!selection.IsGroundwork)
            return;

        if (units.GroupBy(unit => unit.UnitId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new PerformanceContractException("The benchmark composition contains one unit ID in multiple targets; feature classification is unsupported.");

        var unitFeatureCounts = units
            .GroupBy(unit => unit.UnitId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => features.Count(feature => feature.StorageUnitIds.Contains(group.Key, StringComparer.Ordinal)),
                StringComparer.Ordinal);
        if (unitFeatureCounts.Any(pair => pair.Value != 1) ||
            features.SelectMany(feature => feature.StorageUnitIds).Except(unitFeatureCounts.Keys, StringComparer.Ordinal).Any())
            throw new PerformanceContractException("The benchmark composition feature classification does not cover the registry exactly once.");
    }

    public static void Validate(RunRequest request)
    {
        var expected = Describe(request).Fingerprint;
        if (!StringComparer.Ordinal.Equals(expected, request.CompositionFingerprint))
            throw new PerformanceContractException(
                $"The request composition fingerprint '{request.CompositionFingerprint}' does not match " +
                $"the current adapter composition '{expected}'. Rebuild and regenerate the request.");
    }

    private static void ValidateRequestIdentity(RunRequest request)
    {
        if (request.ProviderConfiguration is null || request.PackageVersions is null)
            throw new PerformanceContractException("The benchmark composition metadata is incomplete.");

        var root = SourceProvenance.FindRepositoryRoot();
        var workloadCatalog = WorkloadCatalog.Load(root);
        if (!workloadCatalog.Workloads.TryGetValue(request.WorkloadId, out var workload))
            throw new PerformanceContractException($"Workload '{request.WorkloadId}' is not in the frozen catalog.");
        if (string.Equals(request.WorkloadId, DiagnosticsDurableHistoryWorkload.WorkloadId, StringComparison.Ordinal))
            ArtifactAdmission.ValidateEvidenceRequest(workload, request);
        else
            ArtifactAdmission.ValidateRequest(workload, request);
        var registration = MatrixCatalog.Build(root).Registrations.SingleOrDefault(item =>
            string.Equals(item.WorkloadId, request.WorkloadId, StringComparison.Ordinal) &&
            string.Equals(item.WorkloadVersion, request.WorkloadVersion, StringComparison.Ordinal) &&
            string.Equals(item.Adapter, request.Adapter, StringComparison.Ordinal) &&
            string.Equals(item.PhysicalForm, request.PhysicalForm, StringComparison.Ordinal) &&
            item.Providers.Contains(request.Provider, StringComparer.Ordinal));
        if (registration is null)
            throw new PerformanceContractException(
                $"No current benchmark registration matches '{request.WorkloadId}/{request.WorkloadVersion}/" +
                $"{request.Adapter}/{request.PhysicalForm}/{request.Provider}'.");

        if (request.ProviderConfiguration.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) ||
                pair.Key.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                pair.Value.Contains('\r') || pair.Value.Contains('\n')))
            throw new PerformanceContractException(
                "The benchmark composition provider settings are missing or contain connection material.");
        if (request.PackageVersions.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
            throw new PerformanceContractException("The benchmark composition package metadata is incomplete.");
        ProviderPackageProvenance.RequireExactCurrent(
            root,
            request.Adapter,
            request.Provider,
            request.PackageVersions);
    }

    internal sealed record BenchmarkCompositionDescriptor(
        int FormatVersion,
        bool IsGroundwork,
        string WorkloadId,
        string WorkloadVersion,
        string Adapter,
        string PhysicalForm,
        string Provider,
        string ProviderVersion,
        string ProviderTopology,
        IReadOnlyList<BenchmarkNameValue> ProviderConfiguration,
        IReadOnlyList<BenchmarkNameValue> PackageVersions,
        IReadOnlyList<BenchmarkFeatureDescriptor> Features,
        IReadOnlyList<BenchmarkStorageUnitDescriptor> StorageUnits)
    {
        [JsonIgnore]
        public string Fingerprint => Convert.ToHexString(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(this, ArtifactStore.JsonOptions)))
            .ToLowerInvariant();

        public void Validate()
        {
            if (FormatVersion != BenchmarkCompositionFingerprint.FormatVersion ||
                string.IsNullOrWhiteSpace(WorkloadId) ||
                string.IsNullOrWhiteSpace(WorkloadVersion) ||
                string.IsNullOrWhiteSpace(Adapter) ||
                string.IsNullOrWhiteSpace(PhysicalForm) ||
                string.IsNullOrWhiteSpace(Provider) ||
                string.IsNullOrWhiteSpace(ProviderVersion) ||
                string.IsNullOrWhiteSpace(ProviderTopology) ||
                ProviderConfiguration is null ||
                PackageVersions is null ||
                Features is null ||
                StorageUnits is null ||
                ProviderConfiguration.Count == 0 ||
                PackageVersions.Count == 0 ||
                Features.Count == 0 && IsGroundwork ||
                StorageUnits.Any(unit => string.IsNullOrWhiteSpace(unit.Target) ||
                                         string.IsNullOrWhiteSpace(unit.UnitId) ||
                                         string.IsNullOrWhiteSpace(unit.Name) ||
                                         !IsLowerSha256(unit.Fingerprint)))
                throw new PerformanceContractException("The benchmark composition descriptor is incomplete.");

            if (StorageUnits.Select(unit => (unit.Target, unit.UnitId)).Distinct().Count() != StorageUnits.Count)
                throw new PerformanceContractException("The benchmark composition contains duplicate target/unit identities.");
            if (Features.Select(feature => feature.Id).Distinct(StringComparer.Ordinal).Count() != Features.Count)
                throw new PerformanceContractException("The benchmark composition contains duplicate feature identities.");
            if (Features.Any(feature => string.IsNullOrWhiteSpace(feature.Id) ||
                                        string.IsNullOrWhiteSpace(feature.SchemaIdentity) ||
                                        feature.StorageUnitIds.Any(string.IsNullOrWhiteSpace) ||
                                        feature.StorageUnitIds.Distinct(StringComparer.Ordinal).Count() != feature.StorageUnitIds.Count ||
                                        feature.Id.StartsWith("groundwork-", StringComparison.Ordinal) && feature.StorageUnitIds.Count == 0))
                throw new PerformanceContractException("The benchmark composition contains an incomplete feature identity.");
            if (StorageUnits.Any(unit => unit.SchemaVersion <= 0))
                throw new PerformanceContractException("The benchmark composition contains an invalid schema version.");
            if (ProviderConfiguration.Select(pair => pair.Name).Distinct(StringComparer.Ordinal).Count() != ProviderConfiguration.Count ||
                PackageVersions.Select(pair => pair.Name).Distinct(StringComparer.Ordinal).Count() != PackageVersions.Count)
                throw new PerformanceContractException("The benchmark composition contains duplicate metadata keys.");
        }
    }

    internal sealed record BenchmarkCompositionDocument(
        int FormatVersion,
        string Fingerprint,
        BenchmarkCompositionDescriptor Descriptor);

    private static bool IsLowerSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal sealed record BenchmarkFeatureDescriptor(
        string Id,
        string SchemaIdentity,
        IReadOnlyList<string> StorageUnitIds);

    internal sealed record BenchmarkStorageUnitDescriptor(
        string Target,
        string UnitId,
        string Name,
        int SchemaVersion,
        string Fingerprint);

    internal sealed record BenchmarkNameValue(string Name, string Value);

    internal sealed record CompositionSelection(
        bool IsGroundwork,
        bool IncludeDistributed,
        bool IncludeIdentity,
        bool IncludeSecrets,
        bool IncludeDiagnostics,
        IReadOnlyList<BenchmarkFeatureDescriptor> Features)
    {
        public static CompositionSelection For(RunRequest request)
        {
            var groundwork = request.Adapter switch
            {
                BenchmarkAdapterRegistry.GroundworkV2Adapter => true,
                BenchmarkAdapterRegistry.GroundworkAspNetCoreIdentityAdapter => true,
                BenchmarkAdapterRegistry.GroundworkSecretRepositoryAdapterId => true,
                BenchmarkAdapterRegistry.EfSecretRepositoryAdapterId => false,
                BenchmarkAdapterRegistry.EfDiagnosticsAdapterId => false,
                _ => throw new PerformanceContractException(
                    $"No composition descriptor is registered for adapter '{request.Adapter}'.")
            };

            if (!groundwork)
            {
                var feature = request.Adapter switch
                {
                    BenchmarkAdapterRegistry.EfSecretRepositoryAdapterId =>
                        new BenchmarkFeatureDescriptor("ef-secret-repository", "efcore-secret-model-v1", ["ef.Secret"]),
                    BenchmarkAdapterRegistry.EfDiagnosticsAdapterId =>
                        new BenchmarkFeatureDescriptor("ef-diagnostics-oracle", "efcore-diagnostics-model-v1", ["ef.StructuredLogs", "ef.OpenTelemetry"]),
                    _ => throw new PerformanceContractException($"No EF composition descriptor is registered for adapter '{request.Adapter}'.")
                };
                return new(false, false, false, false, false, [feature]);
            }

            var includeDistributed = request.WorkloadId is
                "placement-takeover" or "command-send-lease-ack";
            var includeIdentity = string.Equals(request.Adapter, BenchmarkAdapterRegistry.GroundworkAspNetCoreIdentityAdapter, StringComparison.Ordinal);
            var includeSecrets = string.Equals(request.Adapter, BenchmarkAdapterRegistry.GroundworkSecretRepositoryAdapterId, StringComparison.Ordinal);
            var includeDiagnostics = string.Equals(request.WorkloadId, DiagnosticsDurableHistoryWorkload.WorkloadId, StringComparison.Ordinal) &&
                                     string.Equals(request.Adapter, DiagnosticsDurableHistoryAdapter.AdapterId, StringComparison.Ordinal);
            var features = new List<BenchmarkFeatureDescriptor>
            {
                new("groundwork-runtime", "elsa-runtime-v2-schema-v1", ElsaRuntimeV2StorageManifest.CreateUnits().Select(unit => unit.Id.Value).ToArray())
            };
            if (includeDistributed)
                features.Add(new("groundwork-distributed-runtime", "elsa-distributed-runtime-v2-schema-v1", DistributedGroundworkStorageManifest.CreateUnits().Select(unit => unit.Id.Value).ToArray()));
            if (includeIdentity)
                features.Add(new("groundwork-foundation-identity", "elsa-foundation-identity-v2-schema-v1", IdentityV2StorageManifest.CreateUnits().Select(unit => unit.Id.Value).ToArray()));
            if (includeSecrets)
                features.Add(new("groundwork-secrets", "elsa-secrets-v2-schema-v1", [SecretsGroundworkStorageSchema.UnitId]));
            if (includeDiagnostics)
            {
                var diagnostics = V2OpenTelemetryStorageSchema.CreateUnits()
                    .Concat([StructuredLogsGroundworkStorageSchema.CreateUnit()])
                    .Select(unit => unit.Id.Value)
                    .ToArray();
                features.Add(new("groundwork-diagnostics", "elsa-diagnostics-v2-schema-v1", diagnostics));
            }
            return new(true, includeDistributed, includeIdentity, includeSecrets, includeDiagnostics, features);
        }
    }
}
