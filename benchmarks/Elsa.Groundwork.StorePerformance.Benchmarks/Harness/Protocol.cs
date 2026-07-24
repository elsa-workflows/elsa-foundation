using System.Diagnostics;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>Recovered and generalized from the Spec 093 harness protocol (commit 30ec15491): one untimed
/// warm-up child process followed by three independent measured child processes. The 093 design-only
/// targets and its superseded absolute-budget amendment are intentionally not carried forward.</summary>
public sealed record BenchmarkProtocol(int WarmupProcessCount, int MeasuredProcessCount, int MinimumOperations, TimeSpan MinimumSteadyState, int WarmupOperations)
{
    public static readonly BenchmarkProtocol Acceptance = new(1, 3, 100, TimeSpan.FromSeconds(30), 50);
    public void Validate() { if (WarmupProcessCount != 1 || MeasuredProcessCount != 3 || MinimumOperations < 100 || MinimumSteadyState < TimeSpan.FromSeconds(30)) throw new PerformanceContractException("The #646 acceptance protocol is fixed to one warm-up, three measured processes, >=100 operations, and >=30 seconds."); }
}

public enum ProcessKind { Warmup, Measured }
public sealed record RunRequest(
    string ComparisonCohortId,
    string MeasurementSetId,
    string WorkloadId,
    string WorkloadVersion,
    string Provider,
    string Adapter,
    string PhysicalForm,
    string Scale,
    string CommitSha,
    IReadOnlyDictionary<string, string> PackageVersions,
    string CompositionFingerprint,
    string Seed,
    string InputFingerprintSha256,
    string NativePlanIdentity,
    string NativePlanEvidenceReference,
    string NativePlanContentSha256,
    ProcessKind ProcessKind,
    int ProcessIndex);
public sealed record NativeRouteEvidence(
    string RouteIdentity,
    string ContentSha256,
    string PlanClassification,
    string IndexName,
    int PhysicalCardinality,
    bool HasStorageScopePredicate,
    bool HasRoutePredicate,
    int FiniteLimit,
    int MaterializedCandidateCount);
public sealed record NativePlanEvidenceDocument(
    int SchemaVersion,
    string ComparisonCohortId,
    string MeasurementSetId,
    string WorkloadId,
    string WorkloadVersion,
    string Provider,
    string Adapter,
    string PhysicalForm,
    string Scale,
    string CommitSha,
    string CompositionFingerprint,
    string Identity,
    IReadOnlyList<NativeRouteEvidence> Routes);
public sealed record NativePlanEvidence(string Identity, string Reference, string ContentSha256, IReadOnlyList<NativeRouteEvidence> Routes);
public sealed record CorrectnessEvidence(string ObservedResultDigestSha256, string ProviderPrerequisite, NativePlanEvidence NativePlan);
public sealed record OperationSample(string Operation, int Count, double SteadyStateSeconds, double ThroughputPerSecond, double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, IReadOnlyList<double> RawLatenciesMilliseconds);
public sealed record ProcessArtifact(int SchemaVersion, RunRequest Request, BenchmarkProtocol Protocol, bool CorrectnessPassed, CorrectnessEvidence Correctness, IReadOnlyList<OperationSample> Operations, MachineMetadata Machine);
public sealed record MachineMetadata(string OperatingSystem, string Runtime, string ProcessArchitecture, string OperatingSystemArchitecture, int ProcessorCount, string TimestampUtc);

/// <summary>Implemented by real EF and Groundwork provider adapter leaves. No adapter ships in this project;
/// a missing adapter is a blocked run, never a simulated result.</summary>
public interface IBenchmarkAdapter : IAsyncDisposable
{
    Task PrepareAsync(CancellationToken cancellationToken);
    Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken);
    IReadOnlyList<IBenchmarkOperation> Operations { get; }
}
public interface IBenchmarkOperation { string Id { get; } Task InvokeAsync(long invocation, CancellationToken cancellationToken); }

public static class ProcessMeasurement
{
    public static async Task<ProcessArtifact> ExecuteAsync(PerformanceWorkload workload, RunRequest request, BenchmarkProtocol protocol, IBenchmarkAdapter adapter, string outputDirectory, CancellationToken cancellationToken)
    {
        protocol.Validate();
        ArtifactAdmission.ValidateRequest(workload, request);
        await adapter.PrepareAsync(cancellationToken);
        var correctness = await adapter.VerifyCorrectnessAsync(cancellationToken);
        ArtifactAdmission.ValidateCorrectness(workload, request, correctness, outputDirectory);
        var operations = new List<OperationSample>();
        foreach (var operation in adapter.Operations)
        {
            if (request.ProcessKind == ProcessKind.Warmup)
                await WarmAsync(operation, protocol.WarmupOperations, cancellationToken);
            else
                operations.Add(await MeasureAsync(operation, protocol, cancellationToken));
        }
        return new ProcessArtifact(2, request, protocol, true, correctness, operations, new MachineMetadata(System.Runtime.InteropServices.RuntimeInformation.OSDescription, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(), Environment.ProcessorCount, DateTimeOffset.UtcNow.ToString("O")));
    }

    private static async Task WarmAsync(IBenchmarkOperation operation, int count, CancellationToken token) { for (var i = 0; i < count; i++) await operation.InvokeAsync(-1L - i, token); }
    private static async Task<OperationSample> MeasureAsync(IBenchmarkOperation operation, BenchmarkProtocol protocol, CancellationToken token)
    {
        var samples = new List<double>();
        var stopwatch = Stopwatch.StartNew();
        for (var invocation = 0L; samples.Count < protocol.MinimumOperations || stopwatch.Elapsed < protocol.MinimumSteadyState; invocation++)
        {
            var start = Stopwatch.GetTimestamp();
            await operation.InvokeAsync(invocation, token);
            samples.Add(Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 4));
        }
        var elapsed = stopwatch.Elapsed.TotalSeconds;
        return new OperationSample(operation.Id, samples.Count, elapsed, elapsed > 0 ? samples.Count / elapsed : 0, Statistics.Percentile(samples, 50), Statistics.Percentile(samples, 95), Statistics.Percentile(samples, 99), samples);
    }
}

public static class ArtifactAdmission
{
    public static void Validate(PerformanceWorkload workload, ProcessArtifact artifact, string outputDirectory)
    {
        if (artifact.SchemaVersion != 2 || artifact.Protocol != BenchmarkProtocol.Acceptance || !artifact.CorrectnessPassed)
            throw new PerformanceContractException("Process artifacts require schema v2, the acceptance protocol, and passing correctness evidence.");
        ValidateRequest(workload, artifact.Request);
        if (artifact.Correctness is null || artifact.Machine is null || artifact.Operations is null)
            throw new PerformanceContractException("Process artifacts require correctness, machine, and operation fields.");
        ValidateCorrectness(workload, artifact.Request, artifact.Correctness, outputDirectory);
        if (!ValidMachine(artifact.Machine))
            throw new PerformanceContractException("Process artifacts require complete machine metadata.");
        if (artifact.Request.ProcessKind == ProcessKind.Warmup)
        {
            if (artifact.Request.ProcessIndex != 0 || artifact.Operations.Count != 0)
                throw new PerformanceContractException("Warmup artifacts must use index 0 and contain no timed samples.");
        }
        else if (artifact.Request.ProcessIndex is < 1 or > 3 ||
                 artifact.Operations.Count == 0 ||
                 artifact.Operations.Any(operation =>
                     operation.Count < BenchmarkProtocol.Acceptance.MinimumOperations ||
                     operation.SteadyStateSeconds < BenchmarkProtocol.Acceptance.MinimumSteadyState.TotalSeconds ||
                     !Statistics.HasAuthoritativeRawMetrics(operation)))
            throw new PerformanceContractException("Measured artifacts require accepted process indices and authoritative finite positive raw samples with summaries reproduced from them.");
        ArtifactSafety.Validate(artifact);
    }

    public static void ValidateRequest(PerformanceWorkload workload, RunRequest request)
    {
        ArtifactSafety.ValidateRequest(request);
        if (workload.Id != request.WorkloadId ||
            workload.Version != request.WorkloadVersion ||
            workload.Input.Seed != request.Seed ||
            workload.Input.FingerprintSha256 != request.InputFingerprintSha256 ||
            !workload.RequiredProviders.Contains(request.Provider, StringComparer.Ordinal) ||
            !workload.PhysicalFormsFor646.Contains(request.PhysicalForm, StringComparer.Ordinal))
            throw new PerformanceContractException("The run request does not match the frozen workload/provider/form contract.");
    }

    public static void ValidateCorrectness(PerformanceWorkload workload, RunRequest request, CorrectnessEvidence evidence, string outputDirectory)
    {
        var nativePlan = evidence.NativePlan;
        if (evidence.ObservedResultDigestSha256 != workload.Correctness.ResultDigestSha256 ||
            string.IsNullOrWhiteSpace(evidence.ProviderPrerequisite) ||
            evidence.ProviderPrerequisite != workload.RequiredProviderEvidence[request.Provider] ||
            nativePlan is null ||
            nativePlan.Identity != request.NativePlanIdentity ||
            nativePlan.Reference != request.NativePlanEvidenceReference ||
            nativePlan.ContentSha256 != request.NativePlanContentSha256 ||
            !IsSha256(nativePlan.ContentSha256))
            throw new PerformanceContractException("Correctness and native-plan evidence must be bound to the exact requested identity, reference, and content digest before timing.");
        var evidencePath = ArtifactStore.EvidencePath(outputDirectory, nativePlan.Reference);
        if (!File.Exists(evidencePath) || ArtifactStore.HashFile(evidencePath) != nativePlan.ContentSha256)
            throw new PerformanceContractException("Native-plan evidence file is missing or does not match the requested content digest.");

        var routes = nativePlan.Routes;
        if (routes is null ||
            routes.Count != workload.RequiredNativeRoutes.Count ||
            !routes.Select(route => route.RouteIdentity).Order(StringComparer.Ordinal).SequenceEqual(workload.RequiredNativeRoutes.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            routes.Select(route => route.RouteIdentity).Distinct(StringComparer.Ordinal).Count() != routes.Count ||
            routes.Any(route =>
                !IsSha256(route.ContentSha256) ||
                string.IsNullOrWhiteSpace(route.PlanClassification) ||
                string.IsNullOrWhiteSpace(route.IndexName) ||
                route.PhysicalCardinality <= 0 ||
                !route.HasStorageScopePredicate ||
                !route.HasRoutePredicate ||
                route.FiniteLimit <= 0 ||
                route.MaterializedCandidateCount <= 0 ||
                route.MaterializedCandidateCount > route.FiniteLimit))
            throw new PerformanceContractException("Native-plan evidence must admit every required route with content digest, bounded cardinality, predicates, finite limit, and materialized-count facts.");
        NativePlanEvidenceDocument document;
        try
        {
            var bytes = File.ReadAllBytes(evidencePath);
            using var json = System.Text.Json.JsonDocument.Parse(bytes);
            ArtifactStore.RejectDuplicateProperties(json.RootElement);
            document = System.Text.Json.JsonSerializer.Deserialize<NativePlanEvidenceDocument>(bytes, ArtifactStore.JsonOptions)
                       ?? throw new PerformanceContractException("Native-plan evidence document is invalid.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new PerformanceContractException($"Native-plan evidence document JSON is invalid: {exception.Message}");
        }
        if (document.SchemaVersion != 2 ||
            document.ComparisonCohortId != request.ComparisonCohortId ||
            document.MeasurementSetId != request.MeasurementSetId ||
            document.WorkloadId != request.WorkloadId ||
            document.WorkloadVersion != request.WorkloadVersion ||
            document.Provider != request.Provider ||
            document.Adapter != request.Adapter ||
            document.PhysicalForm != request.PhysicalForm ||
            document.Scale != request.Scale ||
            document.CommitSha != request.CommitSha ||
            document.CompositionFingerprint != request.CompositionFingerprint ||
            document.Identity != nativePlan.Identity ||
            document.Routes is null ||
            !document.Routes.SequenceEqual(routes))
            throw new PerformanceContractException("Native-plan evidence file does not match the admitted target, provenance, or structured route evidence.");
        ArtifactSafety.Validate(evidence);
    }

    public static bool SameMachineEnvironment(MachineMetadata first, MachineMetadata second) =>
        first.OperatingSystem == second.OperatingSystem &&
        first.Runtime == second.Runtime &&
        first.ProcessArchitecture == second.ProcessArchitecture &&
        first.OperatingSystemArchitecture == second.OperatingSystemArchitecture &&
        first.ProcessorCount == second.ProcessorCount;

    private static bool ValidMachine(MachineMetadata machine) =>
        !string.IsNullOrWhiteSpace(machine.OperatingSystem) &&
        !string.IsNullOrWhiteSpace(machine.Runtime) &&
        !string.IsNullOrWhiteSpace(machine.ProcessArchitecture) &&
        !string.IsNullOrWhiteSpace(machine.OperatingSystemArchitecture) &&
        machine.ProcessorCount > 0 &&
        DateTimeOffset.TryParse(machine.TimestampUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _);
    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class PerformanceContractException(string message) : Exception(message);
