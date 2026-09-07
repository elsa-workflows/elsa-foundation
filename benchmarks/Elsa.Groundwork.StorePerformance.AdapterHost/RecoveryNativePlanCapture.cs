using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Persistence.Groundwork.Runtime;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>Captures the four provider-native recovery routes from the production paging scanner.</summary>
internal static class RecoveryNativePlanCapture
{
    private const string RouteContract = "provider-native-routes";
    public static async Task<string> CaptureAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observed);
        EnsureRequest(request, observed);

        var observer = new WritePathRoundTripObserver(request.Provider, captureCommands: true);
        var persistenceScope = RecoveryScanAdapter.PersistenceScopeFor(request);
        await using var composition = await RuntimeStoreComposition.CreateAsync(
            request.Provider,
            connectionString,
            persistenceScope,
            cancellationToken,
            observer);
        var workload = new RuntimeRecoveryScanWorkload();
        await workload.ExecuteAsync(new CaptureAdapter(composition, persistenceScope), cancellationToken);

        var explainDirectory = Path.Combine(
            Path.GetTempPath(),
            $"groundwork-recovery-explain-{request.Provider}-{request.MeasurementSetId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(explainDirectory);
        var previousFlag = Environment.GetEnvironmentVariable("GW_EXPLAIN_ASSERT");
        var previousDirectory = Environment.GetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR");
        try
        {
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", "1");
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", explainDirectory);
            observer.ClearCommands();
            var client = composition.CreateRecoveryScanClient();
            var page = await client.Scanner.ScanPageAsync(
                new Elsa.Workflows.Runtime.Core.Models.RuntimeRecoveryScanRequest(
                    RuntimeRecoveryScanWorkload.FixedNowUtc,
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(1),
                    RuntimeRecoveryScanWorkload.PageSize),
                cancellationToken);
            if (page.Items.Count != RuntimeRecoveryScanWorkload.PageSize)
                throw new PerformanceContractException("Recovery native-plan capture did not materialize the frozen bounded page.");

            var evidence = new List<NativeRouteEvidence>(RuntimeRecoveryScanWorkload.NativeRouteIdentities.Count);
            foreach (var route in RuntimeRecoveryScanWorkload.NativeRouteIdentities)
            {
                var specification = RecoveryRetainedNativePlan.Definition(route);
                var command = RequireRouteCommand(observer.Commands, specification, route, request.Provider);
                var nativePlanPath = RequireNativePlanArtifact(explainDirectory, request.Provider, specification.IndexName, route);
                var parsed = IamNativePlanParser.Parse(request.Provider, File.ReadAllText(nativePlanPath));
                var normalized = IamNativePlanParser.NormalizeForArtifact(request.Provider, parsed.Content);
                parsed = IamNativePlanParser.Parse(request.Provider, normalized);
                var retained = RecoveryRetainedNativePlan.Create(
                    request.Provider,
                    route,
                    command.CommandText!,
                    normalized);
                var rawReference = ArtifactStore.RawPlanName(
                    $"recovery.{request.Provider}.{request.MeasurementSetId}.{route}.raw{IamNativePlanParser.RawPlanExtension(request.Provider)}");
                var rawPath = Path.Combine(outputDirectory, rawReference);
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(rawPath, retained);
                ArtifactStore.ValidateRawPlanFile(rawPath);
                evidence.Add(new NativeRouteEvidence(
                    route,
                    rawReference,
                    NativePlanEvidenceStaging.Sha256(rawPath),
                    parsed.PlanClassification,
                    parsed.PhysicalIndexName,
                    RuntimeRecoveryScanWorkload.ExecutionCount,
                    command.CommandText?.Contains("__groundwork_scope", StringComparison.Ordinal) == true,
                    command.CommandText?.Contains(specification.PredicateField, StringComparison.Ordinal) == true,
                    1,
                    1));
            }

            return NativePlanEvidenceStaging.Write(
                outputDirectory,
                new NativePlanEvidenceDocument(
                    2, request.ComparisonCohortId, request.MeasurementSetId, request.WorkloadId, request.WorkloadVersion,
                    request.Provider, request.Adapter, request.PhysicalForm, request.Scale, request.CommitSha,
                    request.HarnessAssemblySha256, request.CompositionFingerprint, request.HostFingerprintSha256,
                    observed.Version, observed.Topology, observed.Configuration, request.Seed, request.InputFingerprintSha256,
                    request.NativePlanIdentity, evidence, RouteContract));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", previousFlag);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", previousDirectory);
            try
            {
                if (Directory.Exists(explainDirectory))
                    Directory.Delete(explainDirectory, recursive: true);
            }
            catch
            {
                // Retained raw artifacts are complete; temporary diagnostics cleanup must not mask capture.
            }
        }
    }

    private static void EnsureRequest(RunRequest request, ProviderProbe.Result observed)
    {
        if (request.WorkloadId != RuntimeRecoveryScanWorkload.WorkloadId ||
            request.WorkloadVersion != RuntimeRecoveryScanWorkload.Version ||
            request.PhysicalForm != RecoveryScanAdapter.PhysicalForm ||
            request.NativePlanEvidenceReference != NativePlanEvidenceStaging.ReferenceFor(request.WorkloadId, request.Provider, request.MeasurementSetId) ||
            observed.Provider != request.Provider || observed.Version != request.ProviderVersion ||
            observed.Topology != request.ProviderTopology ||
            !observed.Configuration.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)))
            throw new PerformanceContractException("Recovery native-plan capture request does not match the live provider and frozen route contract.");
    }

    private static ProviderCommandEvent RequireRouteCommand(
        IReadOnlyList<ProviderCommandEvent> commands,
        RecoveryRetainedNativePlan.RouteDefinition specification,
        string route,
        string provider)
    {
        var matches = commands.Where(command =>
                !command.IsProbe && command.Kind == ProviderCommandKind.Read &&
                string.Equals(command.Operation, provider + ".query", StringComparison.Ordinal) &&
                command.CommandText?.Contains(specification.PredicateField, StringComparison.Ordinal) == true)
            .ToArray();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].CommandText))
            throw new PerformanceContractException($"Recovery native route '{route}' must emit exactly one observable provider query for '{specification.PredicateField}'; observed {matches.Length}.");
        return matches[0];
    }

    private static string RequireNativePlanArtifact(string directory, string provider, string indexName, string route)
    {
        var extension = IamNativePlanParser.RawPlanExtension(provider);
        var suffix = $"-{indexName}{extension}";
        var matches = Directory.EnumerateFiles(directory)
            .Where(path => Path.GetFileName(path).EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException($"Recovery native route '{route}' must emit exactly one provider-native explain artifact for logical index '{indexName}'; observed {matches.Length}.");
        return matches[0];
    }

    private sealed class CaptureAdapter(RuntimeStoreComposition composition, string persistenceScope) : IRuntimeRecoveryScanWorkloadAdapter
    {
        public string PersistenceScope => persistenceScope;

        public ValueTask<RuntimeRecoveryScanClient> OpenClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(composition.CreateRecoveryScanClient());
        }

        public ValueTask<RuntimeRecoveryScanClient> ReopenClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(composition.CreateRecoveryScanClient());
        }
    }
}
