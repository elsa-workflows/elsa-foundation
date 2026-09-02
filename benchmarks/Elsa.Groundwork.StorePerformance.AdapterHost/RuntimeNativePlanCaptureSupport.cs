using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

internal static class RuntimeNativePlanCaptureSupport
{
    public static WritePathRoundTripObserver RequireCommandObserver(IProviderRoundTripObserver? observer) =>
        observer as WritePathRoundTripObserver
        ?? throw new PerformanceContractException(
            "Runtime native-plan capture requires the exact Groundwork provider-command observer.");

    public static void EnsureRequest(
        RunRequest request,
        ProviderProbe.Result observed,
        string workloadId,
        string physicalForm)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observed);
        if (!string.Equals(request.WorkloadId, workloadId, StringComparison.Ordinal) ||
            !string.Equals(request.WorkloadVersion, "1.1.0", StringComparison.Ordinal) ||
            !string.Equals(request.Adapter, RuntimeNativePlanContract.GroundworkAdapter, StringComparison.Ordinal) ||
            !string.Equals(request.PhysicalForm, physicalForm, StringComparison.Ordinal) ||
            !string.Equals(request.NativePlanEvidenceReference,
                NativePlanEvidenceStaging.ReferenceFor(request.WorkloadId, request.Provider, request.MeasurementSetId),
                StringComparison.Ordinal) ||
            !string.Equals(observed.Provider, request.Provider, StringComparison.Ordinal) ||
            !string.Equals(observed.Version, request.ProviderVersion, StringComparison.Ordinal) ||
            !string.Equals(observed.Topology, request.ProviderTopology, StringComparison.Ordinal) ||
            !observed.Configuration.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)))
            throw new PerformanceContractException(
                "Runtime native-plan capture request does not match the live provider and frozen route contract.");
    }

    public static ProviderCommandEvent RequireRouteCommand(
        IReadOnlyList<ProviderCommandEvent> commands,
        RuntimeNativeRouteSpec specification)
    {
        var matches = commands.Where(command =>
                !command.IsProbe &&
                command.Kind == ProviderCommandKind.Read &&
                command.Operation.EndsWith(".query", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(command.CommandText) &&
                command.CommandText.Contains(specification.TableName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException(
                $"Runtime route '{specification.RouteIdentity}' must emit exactly one provider query against '{specification.TableName}'; observed {matches.Length}.");
        return matches[0];
    }

    public static string RequireNativeArtifact(
        string directory,
        IReadOnlySet<string> before,
        string provider,
        RuntimeNativeRouteSpec specification)
    {
        var extension = IamNativePlanParser.RawPlanExtension(provider);
        var suffix = $"-{specification.IndexName}{extension}";
        var matches = Directory.EnumerateFiles(directory)
            .Where(path => !before.Contains(path) && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetFileName(path).EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != 1)
        {
            var observed = string.Join(", ", Directory.EnumerateFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            throw new PerformanceContractException(
                $"Runtime route '{specification.RouteIdentity}' must emit exactly one provider-native explain artifact; observed {matches.Length} (files: {observed}).");
        }
        return matches[0];
    }

    public static IamNativePlanParser.ParsedPlan ParsePlan(string provider, string rawPlan)
    {
        // A total-count page may include only Groundwork's named result scans in addition to the
        // route index search. ParseSecret's strict exception is intentionally reused here; it allows
        // those result iterators while still rejecting every physical source scan.
        return provider == "sqlite"
            ? IamNativePlanParser.ParseSecret(provider, rawPlan)
            : IamNativePlanParser.Parse(provider, rawPlan);
    }

    public static string WriteArtifact(
        string outputDirectory,
        RunRequest request,
        RuntimeNativeRouteSpec specification,
        ProviderCommandEvent command,
        IamNativePlanParser.ParsedPlan plan,
        string rawReference)
    {
        if (string.IsNullOrWhiteSpace(command.CommandText))
            throw new PerformanceContractException(
                $"Runtime route '{specification.RouteIdentity}' did not retain provider command text.");

        var normalized = IamNativePlanParser.NormalizeForArtifact(request.Provider, plan.Content);
        plan = ParsePlan(request.Provider, normalized);
        var path = Path.Combine(outputDirectory, rawReference);
        Directory.CreateDirectory(outputDirectory);
        var artifact = new RuntimeNativePlanArtifact(
            1,
            request.Provider,
            request.Adapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            plan.PhysicalIndexName,
            command.CommandText,
            normalized);
        File.WriteAllText(path, JsonSerializer.Serialize(artifact, ArtifactStore.JsonOptions));
        ArtifactStore.ValidateRawPlanFile(path);
        return path;
    }

    public static NativePlanEvidenceDocument CreateDocument(
        RunRequest request,
        ProviderProbe.Result observed,
        IReadOnlyList<NativeRouteEvidence> routes) =>
        new(
            2,
            request.ComparisonCohortId,
            request.MeasurementSetId,
            request.WorkloadId,
            request.WorkloadVersion,
            request.Provider,
            request.Adapter,
            request.PhysicalForm,
            request.Scale,
            request.CommitSha,
            request.HarnessAssemblySha256,
            request.CompositionFingerprint,
            request.HostFingerprintSha256,
            observed.Version,
            observed.Topology,
            observed.Configuration,
            request.Seed,
            request.InputFingerprintSha256,
            request.NativePlanIdentity,
            routes,
            RuntimeNativePlanContract.RouteContract);

    public static string RawReference(RunRequest request, string routeIdentity, string provider) =>
        ArtifactStore.RawPlanName(
            $"{request.WorkloadId}.{provider}.{request.MeasurementSetId}.{routeIdentity}.raw.json");
}
