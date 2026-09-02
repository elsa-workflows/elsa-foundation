using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

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
    string HarnessAssemblySha256,
    IReadOnlyDictionary<string, string> PackageVersions,
    string CompositionFingerprint,
    string HostFingerprintSha256,
    string ProviderVersion,
    string ProviderTopology,
    IReadOnlyDictionary<string, string> ProviderConfiguration,
    string Seed,
    string InputFingerprintSha256,
    string NativePlanIdentity,
    string NativePlanEvidenceReference,
    string NativePlanContentSha256,
    ProcessKind ProcessKind,
    int ProcessIndex);
public sealed record NativeRouteEvidence(
    string RouteIdentity,
    string RawPlanReference,
    string RawPlanSha256,
    string PlanClassification,
    string IndexName,
    int PhysicalCardinality,
    bool HasStorageScopePredicate,
    bool HasRoutePredicate,
    int FiniteLimit,
    int MaterializedCandidateCount,
    RuntimeNativeResultShape ResultShape = RuntimeNativeResultShape.Page,
    int? ScalarResultCount = null,
    bool UsesLatestPerKey = false)
{
    /// <summary>The actual provider row bound, including any continuation lookahead.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int NativeFetchLimit { get; init; }
}
public sealed record DiagnosticsOracleRouteObservation(
    string RouteIdentity,
    IReadOnlyList<string> CommandTexts,
    string NativePlan);
public sealed record SecretProviderConcurrencyEvidence(
    int IndependentClientCount,
    int CompletedContenders,
    int ProviderCommandStartCount,
    bool ProviderCommandOverlapObserved,
    bool ProviderCommandsSerializedByDesign,
    bool EveryContenderIssuedProviderCommands,
    int DistinctPhysicalConnectionCount);
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
    string HarnessAssemblySha256,
    string CompositionFingerprint,
    string HostFingerprintSha256,
    string ProviderVersion,
    string ProviderTopology,
    IReadOnlyDictionary<string, string> ProviderConfiguration,
    string Seed,
    string InputFingerprintSha256,
    string Identity,
    IReadOnlyList<NativeRouteEvidence> Routes,
    string RouteContract = "provider-native-routes",
    IReadOnlyList<string>? BlockedRoutes = null,
    IReadOnlyList<DiagnosticsOracleRouteObservation>? OracleObservations = null)
{
    public SecretProviderConcurrencyEvidence? ProviderConcurrency { get; init; }
}
public sealed record NativePlanEvidence(string Identity, string Reference, string ContentSha256, IReadOnlyList<NativeRouteEvidence> Routes)
{
    public SecretProviderConcurrencyEvidence? ProviderConcurrency { get; init; }
    public string RouteContract { get; init; } = "provider-native-routes";
    public IReadOnlyList<string> BlockedRoutes { get; init; } = [];
    public IReadOnlyList<DiagnosticsOracleRouteObservation> OracleObservations { get; init; } = [];
}
public sealed record CorrectnessEvidence(
    string ObservedResultDigestSha256,
    string ObservedProviderVersion,
    string ObservedProviderTopology,
    IReadOnlyDictionary<string, string> ObservedProviderConfiguration,
    NativePlanEvidence NativePlan);
public sealed record OperationSample(
    string Operation,
    int Count,
    double SteadyStateSeconds,
    double ThroughputPerSecond,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    IReadOnlyList<double> RawLatenciesMilliseconds)
{
    /// <summary>Total provider-native commands observed for this operation's timed invocations.</summary>
    public long RoundTrips { get; init; }

    /// <summary>One exact provider-command count for every raw latency sample.</summary>
    public IReadOnlyList<long> RawRoundTrips { get; init; } = [];
}
public sealed record ProcessArtifact(int SchemaVersion, RunRequest Request, BenchmarkProtocol Protocol, bool CorrectnessPassed, CorrectnessEvidence Correctness, IReadOnlyList<OperationSample> Operations, MachineMetadata Machine)
{
    /// <summary>Identity of the provider-native observer that produced measured command counts.</summary>
    public string? RoundTripInstrumentation { get; init; }
}
public sealed record MachineMetadata(string OperatingSystem, string Runtime, string ProcessArchitecture, string OperatingSystemArchitecture, int ProcessorCount, string HostFingerprintSha256, string TimestampUtc);

/// <summary>
/// Counts commands issued by the provider connection(s) used by the public adapter.
/// Implementations must count provider-native commands, not adapter method calls or synthetic estimates.
/// </summary>
public interface IProviderRoundTripObserver
{
    string Provider { get; }
    string Instrumentation { get; }
    bool IsExact { get; }
    long Snapshot();
}

public static class HostFingerprint
{
    public static string CaptureSha256()
    {
        var source = CaptureSource();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    private static string CaptureSource()
    {
        if (OperatingSystem.IsLinux())
        {
            foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
            {
                if (!File.Exists(path)) continue;
                var value = File.ReadAllText(path).Trim();
                if (value.Length > 0) return $"linux:{value}";
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var start = new ProcessStartInfo("/usr/sbin/ioreg")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("-rd1");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("IOPlatformExpertDevice");
            using var process = Process.Start(start);
            if (process is not null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                var match = Regex.Match(output, "\"IOPlatformUUID\"\\s*=\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant);
                if (process.ExitCode == 0 && match.Success) return $"macos:{match.Groups[1].Value}";
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                "MachineGuid",
                null) as string;
            if (!string.IsNullOrWhiteSpace(value)) return $"windows:{value}";
        }

        throw new PerformanceContractException("A stable OS machine identity is unavailable; same-host benchmark evidence cannot be produced.");
    }
}

/// <summary>Implemented by real EF and Groundwork provider adapter leaves. No adapter ships in this project;
/// a missing adapter is a blocked run, never a simulated result.</summary>
public interface IBenchmarkAdapter : IAsyncDisposable
{
    Task PrepareAsync(CancellationToken cancellationToken);
    Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken);
    IReadOnlyList<IBenchmarkOperation> Operations { get; }

    /// <summary>Exact provider-native command observer used by measured artifacts.</summary>
    IProviderRoundTripObserver? RoundTripObserver => null;
}
public interface IBenchmarkOperation
{
    string Id { get; }

    /// <summary>
    /// Creates any invocation-specific fixture outside the timing window. The default keeps existing
    /// read-only/idempotent operations source-compatible; mutating leaves use this to avoid measuring reset
    /// writes as part of the named public operation.
    /// </summary>
    Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) => Task.CompletedTask;

    Task InvokeAsync(long invocation, CancellationToken cancellationToken);
}

public static class ProcessMeasurement
{
    public static async Task<ProcessArtifact> ExecuteAsync(PerformanceWorkload workload, RunRequest request, BenchmarkProtocol protocol, IBenchmarkAdapter adapter, string outputDirectory, CancellationToken cancellationToken)
    {
        protocol.Validate();
        ArtifactAdmission.ValidateRequest(workload, request);
        var hostFingerprint = HostFingerprint.CaptureSha256();
        if (hostFingerprint != request.HostFingerprintSha256)
            throw new PerformanceContractException("The adapter child process is not running on the matrix host.");
        SourceProvenance.RequireCleanHead(SourceProvenance.FindRepositoryRoot(), request.CommitSha);
        SourceProvenance.RequireHarnessAssembly(request.HarnessAssemblySha256);
        await adapter.PrepareAsync(cancellationToken);

        IProviderRoundTripObserver? observer = null;
        if (request.ProcessKind == ProcessKind.Measured)
        {
            observer = adapter.RoundTripObserver;
            if (observer is null || !observer.IsExact)
                throw new PerformanceContractException(
                    $"Measured workload '{request.WorkloadId}' requires an exact provider-native round-trip observer; " +
                    "adapter command counts or synthetic estimates are not admissible.");
            if (!string.Equals(observer.Provider, request.Provider, StringComparison.Ordinal))
                throw new PerformanceContractException(
                    $"The round-trip observer targets provider '{observer.Provider}', not requested provider '{request.Provider}'.");
        }
        var correctness = await adapter.VerifyCorrectnessAsync(cancellationToken);
        ArtifactAdmission.ValidateCorrectness(workload, request, correctness, outputDirectory);
        var operations = new List<OperationSample>();
        foreach (var operation in adapter.Operations)
        {
            if (request.ProcessKind == ProcessKind.Warmup)
                await WarmAsync(operation, protocol.WarmupOperations, cancellationToken);
            else
                operations.Add(await MeasureAsync(operation, protocol, observer!, cancellationToken));
        }
        return new ProcessArtifact(2, request, protocol, true, correctness, operations, new MachineMetadata(System.Runtime.InteropServices.RuntimeInformation.OSDescription, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(), Environment.ProcessorCount, hostFingerprint, DateTimeOffset.UtcNow.ToString("O")))
        {
            RoundTripInstrumentation = observer?.Instrumentation
        };
    }

    private static async Task WarmAsync(IBenchmarkOperation operation, int count, CancellationToken token)
    {
        for (var i = 0; i < count; i++)
            await InvokePreparedAsync(operation, -1L - i, static () => { }, token);
    }

    private static async Task<OperationSample> MeasureAsync(
        IBenchmarkOperation operation,
        BenchmarkProtocol protocol,
        IProviderRoundTripObserver observer,
        CancellationToken token)
    {
        var samples = new List<double>();
        var roundTrips = new List<long>();
        var measuredElapsed = TimeSpan.Zero;
        for (var invocation = 0L; ShouldContinue(samples.Count, measuredElapsed, protocol); invocation++)
        {
            var before = 0L;
            var start = 0L;
            // The callback runs after per-invocation fixture setup, so setup commands stay outside both
            // the stopwatch and the provider-command sample for the named public operation.
            await InvokePreparedAsync(operation, invocation, () =>
            {
                before = observer.Snapshot();
                start = Stopwatch.GetTimestamp();
            }, token);
            var elapsed = Stopwatch.GetElapsedTime(start);
            var after = observer.Snapshot();
            var invocationRoundTrips = after - before;
            if (invocationRoundTrips <= 0)
                throw new PerformanceContractException(
                    $"Provider-native round-trip observation recorded no commands for operation '{operation.Id}' invocation {invocation}.");
            measuredElapsed += elapsed;
            samples.Add(Math.Round(elapsed.TotalMilliseconds, 4));
            roundTrips.Add(invocationRoundTrips);
        }
        var measuredSeconds = measuredElapsed.TotalSeconds;
        return new OperationSample(operation.Id, samples.Count, measuredSeconds, measuredSeconds > 0 ? samples.Count / measuredSeconds : 0, Statistics.Percentile(samples, 50), Statistics.Percentile(samples, 95), Statistics.Percentile(samples, 99), samples)
        {
            RoundTrips = roundTrips.Sum(),
            RawRoundTrips = roundTrips
        };
    }

    internal static bool ShouldContinueForTest(
        int operationCount,
        TimeSpan measuredElapsed,
        BenchmarkProtocol protocol) =>
        ShouldContinue(operationCount, measuredElapsed, protocol);

    internal static Task InvokeOnceForTestAsync(
        IBenchmarkOperation operation,
        long invocation,
        Action timingStarted,
        CancellationToken cancellationToken) =>
        InvokePreparedAsync(operation, invocation, timingStarted, cancellationToken);

    private static async Task InvokePreparedAsync(
        IBenchmarkOperation operation,
        long invocation,
        Action timingStarted,
        CancellationToken cancellationToken)
    {
        await operation.PrepareInvocationAsync(invocation, cancellationToken);
        timingStarted();
        await operation.InvokeAsync(invocation, cancellationToken);
    }

    private static bool ShouldContinue(
        int operationCount,
        TimeSpan measuredElapsed,
        BenchmarkProtocol protocol) =>
        operationCount < protocol.MinimumOperations || measuredElapsed < protocol.MinimumSteadyState;
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
        if (artifact.Machine.HostFingerprintSha256 != artifact.Request.HostFingerprintSha256)
            throw new PerformanceContractException("Process artifact machine metadata does not match the matrix host fingerprint.");
        if (artifact.Request.ProcessKind == ProcessKind.Warmup)
        {
            if (artifact.Request.ProcessIndex != 0 || artifact.Operations.Count != 0)
                throw new PerformanceContractException("Warmup artifacts must use index 0 and contain no timed samples.");
        }
        else if (artifact.Request.ProcessIndex is < 1 or > 3 ||
                 artifact.Operations.Count == 0 ||
                 string.IsNullOrWhiteSpace(artifact.RoundTripInstrumentation) ||
                 artifact.Operations.Any(operation =>
                     operation.Count < BenchmarkProtocol.Acceptance.MinimumOperations ||
                     operation.SteadyStateSeconds < BenchmarkProtocol.Acceptance.MinimumSteadyState.TotalSeconds ||
                     !Statistics.HasAuthoritativeRawMetrics(operation)))
            throw new PerformanceContractException("Measured artifacts require an identified exact provider-native round-trip observer and authoritative finite positive raw samples with summaries reproduced from them.");
        ArtifactSafety.Validate(artifact);
    }

    public static void ValidateRequest(PerformanceWorkload workload, RunRequest request) =>
        ValidateRequest(workload, request, allowDiagnosticsEvidence: false);

    /// <summary>
    /// Validates an untimed diagnostics evidence request. Capture and correctness are allowed to compose
    /// a provider while the numeric timing gate remains blocked, but this narrow exception retains every
    /// request, adapter, provenance, topology, fingerprint, and artifact-safety check. It is not used by
    /// matrix/run/compare/gate paths, which continue to require normal benchmark readiness.
    /// </summary>
    public static void ValidateEvidenceRequest(PerformanceWorkload workload, RunRequest request)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(workload.Id, DiagnosticsDurableHistoryWorkload.WorkloadId, StringComparison.Ordinal) ||
            !string.Equals(request.WorkloadId, DiagnosticsDurableHistoryWorkload.WorkloadId, StringComparison.Ordinal))
        {
            ValidateRequest(workload, request, allowDiagnosticsEvidence: false);
            return;
        }

        ValidateRequest(workload, request, allowDiagnosticsEvidence: true);
    }

    private static void ValidateRequest(PerformanceWorkload workload, RunRequest request, bool allowDiagnosticsEvidence)
    {
        if (!allowDiagnosticsEvidence)
            BenchmarkAdmissionGuard.RequireReady(workload);
        else if (!ReproducibleWorkloadScenarioCatalog.TryGetBlockedReason(workload.Id, out var blockedReason) ||
                 !string.Equals(blockedReason, ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, StringComparison.Ordinal))
            throw new PerformanceContractException("Diagnostics evidence bypass is only valid for the reviewed diagnostics absolute-budget block.");

        BenchmarkAdapterAdmission.RequireAdmitted(workload, request.Provider, request.Adapter, request.PhysicalForm);
        ArtifactSafety.ValidateRequest(request);
        if (workload.Id != request.WorkloadId ||
            workload.Version != request.WorkloadVersion ||
            workload.Input.Seed != request.Seed ||
            workload.Input.FingerprintSha256 != request.InputFingerprintSha256 ||
            !workload.RequiredProviders.Contains(request.Provider, StringComparer.Ordinal) ||
            !workload.PhysicalFormsFor646.Contains(request.PhysicalForm, StringComparer.Ordinal) ||
            !workload.RequiredProviderEvidence.TryGetValue(request.Provider, out var topology) ||
            topology != request.ProviderTopology)
            throw new PerformanceContractException("The run request does not match the frozen workload/provider/form contract.");
    }

    public static void ValidateCorrectness(PerformanceWorkload workload, RunRequest request, CorrectnessEvidence evidence, string outputDirectory)
    {
        var nativePlan = evidence.NativePlan;
        if (evidence.ObservedResultDigestSha256 != workload.Correctness.ResultDigestSha256 ||
            evidence.ObservedProviderVersion != request.ProviderVersion ||
            evidence.ObservedProviderTopology != request.ProviderTopology ||
            evidence.ObservedProviderTopology != workload.RequiredProviderEvidence[request.Provider] ||
            evidence.ObservedProviderConfiguration is null ||
            !evidence.ObservedProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)) ||
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
        if (routes is null)
            throw new PerformanceContractException("Native-plan evidence must admit every required route with a retained raw-plan digest, bounded cardinality, predicates, finite limit, and materialized-count facts.");
        var diagnosticsWorkload = string.Equals(workload.Id, DiagnosticsDurableHistoryWorkload.WorkloadId, StringComparison.Ordinal);
        var efCorrectnessOnly = diagnosticsWorkload &&
                                string.Equals(request.Adapter, DiagnosticsNativePlanContract.EfAdapter, StringComparison.Ordinal) &&
                                string.Equals(nativePlan.RouteContract, DiagnosticsNativePlanContract.EfCorrectnessOnlyRouteContract, StringComparison.Ordinal);
        var diagnosticsRoutesBlocked = diagnosticsWorkload &&
                                       (string.Equals(nativePlan.RouteContract, DiagnosticsNativePlanContract.BlockedRouteContract, StringComparison.Ordinal) || efCorrectnessOnly);
        var blockedRoutes = nativePlan.BlockedRoutes ?? [];
        ValidateIamNativeRoutes(workload, routes);
        ValidateSecretNativeRoutes(workload, routes);
        ValidateRecoveryNativeRoutes(workload, routes);
        ValidateRuntimeScheduleNativeRoutes(workload, routes);
        ValidateSecretConcurrency(workload, request, nativePlan.ProviderConcurrency);
        if (efCorrectnessOnly && routes.Count != 0)
            throw new PerformanceContractException("The EF diagnostics correctness-only evidence must not claim provider-native bounded routes.");
        var oracleObservations = nativePlan.OracleObservations ?? [];
        if (efCorrectnessOnly &&
            (oracleObservations.Count != workload.RequiredNativeRoutes.Count ||
             !oracleObservations.Select(observation => observation.RouteIdentity).Order(StringComparer.Ordinal)
                 .SequenceEqual(workload.RequiredNativeRoutes.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
             oracleObservations.Any(observation => observation.CommandTexts.Count == 0 ||
                                                   observation.CommandTexts.Any(string.IsNullOrWhiteSpace) ||
                                                   string.IsNullOrWhiteSpace(observation.NativePlan))))
            throw new PerformanceContractException("The EF diagnostics correctness-only evidence must retain every exact observed public-route command and its provider plan; it may not claim those routes as bounded native evidence.");
        if (!efCorrectnessOnly && oracleObservations.Count != 0)
            throw new PerformanceContractException("Oracle-only route observations are valid only for the temporary EF diagnostics correctness path.");
        if (diagnosticsRoutesBlocked &&
            (!blockedRoutes.Any() ||
             routes.Select(route => route.RouteIdentity).Concat(blockedRoutes).Distinct(StringComparer.Ordinal).Count() != workload.RequiredNativeRoutes.Count ||
             !routes.Select(route => route.RouteIdentity).Concat(blockedRoutes).Order(StringComparer.Ordinal).SequenceEqual(workload.RequiredNativeRoutes.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
             blockedRoutes.Any(route => routes.Any(candidate => candidate.RouteIdentity == route))))
            throw new PerformanceContractException("Blocked diagnostics native-plan evidence must account for every required route exactly once.");
        if (!diagnosticsRoutesBlocked && (routes.Count != workload.RequiredNativeRoutes.Count ||
            !routes.Select(route => route.RouteIdentity).Order(StringComparer.Ordinal).SequenceEqual(workload.RequiredNativeRoutes.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            routes.Select(route => route.RouteIdentity).Distinct(StringComparer.Ordinal).Count() != routes.Count ||
            routes.Any(route =>
                !ArtifactStore.SafeRawPlanReference(route.RawPlanReference) ||
                !IsSha256(route.RawPlanSha256) ||
                string.IsNullOrWhiteSpace(route.PlanClassification) ||
                string.IsNullOrWhiteSpace(route.IndexName) ||
                route.PhysicalCardinality <= 0 ||
                !route.HasStorageScopePredicate ||
                (RoutePredicateRequired(workload, route, diagnosticsWorkload) && !route.HasRoutePredicate) ||
                route.ResultShape switch
                {
                    RuntimeNativeResultShape.Page => route.FiniteLimit <= 0 ||
                                                     route.MaterializedCandidateCount <= 0 ||
                                                     route.MaterializedCandidateCount > route.FiniteLimit ||
                                                     route.ScalarResultCount is not null,
                    RuntimeNativeResultShape.ScalarCount => route.FiniteLimit != 0 ||
                                                            route.MaterializedCandidateCount != 0 ||
                                                            route.ScalarResultCount is not > 0,
                    _ => true
                })))
            throw new PerformanceContractException("Native-plan evidence must admit every required route with a retained raw-plan digest, bounded cardinality, predicates, finite page or scalar-count, and materialized-result facts.");
        if (routes.Select(route => route.RawPlanReference).Distinct(StringComparer.Ordinal).Count() != routes.Count)
            throw new PerformanceContractException("Every native route must bind a distinct retained raw provider-plan artifact.");
        ValidateDiagnosticsNativeRoutes(workload, routes);
        foreach (var route in routes)
        {
            var rawPlanPath = ArtifactStore.RawPlanPath(outputDirectory, route.RawPlanReference);
            if (!File.Exists(rawPlanPath) || ArtifactStore.HashFile(rawPlanPath) != route.RawPlanSha256)
                throw new PerformanceContractException($"Raw provider-plan evidence is missing or does not match its digest for route {route.RouteIdentity}.");
            ArtifactStore.ValidateRawPlanFile(rawPlanPath);
            if (diagnosticsWorkload)
                DiagnosticsNativePlanContract.ValidateEnvelope(request.Provider, request.Adapter, route, rawPlanPath);
            if (string.Equals(request.Adapter, RuntimeNativePlanContract.GroundworkAdapter, StringComparison.Ordinal) &&
                workload.Id is (RuntimeBookmarkLookupWorkload.WorkloadId or
                    RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId or
                    DistributedPlacementTakeoverWorkload.WorkloadId or
                    RuntimeDueTimerSelectionWorkload.WorkloadId or
                    RuntimeRecurringScheduleSelectionWorkload.WorkloadId or
                    RuntimeQueueDrainWorkload.WorkloadId or
                    DistributedCommandSendLeaseAckWorkload.WorkloadId))
                RuntimeNativePlanContract.ValidateEnvelope(request.WorkloadId, request.Provider, request.Adapter, route, rawPlanPath);
            if (string.Equals(workload.Id, SecretCreateReadListWorkload.WorkloadId, StringComparison.Ordinal))
                SecretRetainedNativePlan.Validate(
                    request.Provider,
                    request.Adapter,
                    route,
                    File.ReadAllText(rawPlanPath));
            if (string.Equals(workload.Id, RuntimeRecoveryScanWorkload.WorkloadId, StringComparison.Ordinal))
                RecoveryRetainedNativePlan.Validate(
                    request.Provider,
                    route,
                    File.ReadAllText(rawPlanPath));
        }
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
            document.HarnessAssemblySha256 != request.HarnessAssemblySha256 ||
            document.CompositionFingerprint != request.CompositionFingerprint ||
            document.HostFingerprintSha256 != request.HostFingerprintSha256 ||
            document.ProviderVersion != request.ProviderVersion ||
            document.ProviderTopology != request.ProviderTopology ||
            document.ProviderConfiguration is null ||
            !document.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)) ||
            document.Seed != request.Seed ||
            document.InputFingerprintSha256 != request.InputFingerprintSha256 ||
            document.Identity != nativePlan.Identity ||
            document.RouteContract != (efCorrectnessOnly
                ? DiagnosticsNativePlanContract.EfCorrectnessOnlyRouteContract
                : diagnosticsRoutesBlocked
                    ? DiagnosticsNativePlanContract.BlockedRouteContract
                : workload.RequiredNativeRoutes.Count == 0
                    ? "no-native-routes-declared"
                    : "provider-native-routes") ||
            document.ProviderConcurrency != nativePlan.ProviderConcurrency ||
            document.Routes is null ||
            !document.Routes.SequenceEqual(routes) ||
            !(document.BlockedRoutes ?? []).SequenceEqual(blockedRoutes))
            throw new PerformanceContractException("Native-plan evidence file does not match the admitted target, provenance, or structured route evidence.");
        if (efCorrectnessOnly &&
            (document.OracleObservations is null ||
             !document.OracleObservations.SequenceEqual(oracleObservations)))
            throw new PerformanceContractException("EF diagnostics oracle observations do not match the admitted evidence.");
        if (!efCorrectnessOnly && document.OracleObservations is not null && document.OracleObservations.Count != 0)
            throw new PerformanceContractException("Non-EF diagnostics evidence cannot carry oracle-only route observations.");
        ArtifactSafety.Validate(evidence);
    }

    private static void ValidateIamNativeRoutes(
        PerformanceWorkload workload,
        IReadOnlyList<NativeRouteEvidence> routes)
    {
        if (!string.Equals(workload.Id, IamNormalizedLookupWorkload.WorkloadId, StringComparison.Ordinal))
            return;

        var expectedLimits = IamNormalizedLookupWorkload.NativeRouteLimits;
        var routeNamesMatch = workload.RequiredNativeRoutes.Count == expectedLimits.Count &&
                              workload.RequiredNativeRoutes.Order(StringComparer.Ordinal)
                                  .SequenceEqual(expectedLimits.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
                              routes.Count == expectedLimits.Count &&
                              routes.Select(route => route.RouteIdentity).Distinct(StringComparer.Ordinal).Count() == expectedLimits.Count &&
                              routes.All(route => expectedLimits.ContainsKey(route.RouteIdentity));
        var routeFactsMatch = routes.All(route =>
            route.PhysicalCardinality == 100_000 &&
            route.MaterializedCandidateCount == 1 &&
            expectedLimits.TryGetValue(route.RouteIdentity, out var finiteLimit) &&
            route.FiniteLimit == finiteLimit);

        if (!routeNamesMatch || !routeFactsMatch)
            throw new PerformanceContractException(
                "IAM native-plan evidence must contain exactly the five frozen route names and bind physical cardinality 100000, one materialized candidate, and the exact route-specific finite limits.");
    }

    private static bool RoutePredicateRequired(
        PerformanceWorkload workload,
        NativeRouteEvidence route,
        bool diagnosticsWorkload) =>
        !diagnosticsWorkload &&
        !(workload.Id == RuntimeQueueDrainWorkload.WorkloadId &&
          route.RouteIdentity == "list-pending-scheduler-workflow-executions");

    private static void ValidateSecretNativeRoutes(
        PerformanceWorkload workload,
        IReadOnlyList<NativeRouteEvidence> routes)
    {
        if (!string.Equals(workload.Id, SecretCreateReadListWorkload.WorkloadId, StringComparison.Ordinal))
            return;

        var route = routes.Count == 1 && routes[0].RouteIdentity == "list-filtered" ? routes[0] : null;
        if (route is null ||
            route.PhysicalCardinality != SecretCreateReadListWorkload.CanonicalSecretCount +
            SecretCreateReadListWorkload.NoiseSecretCount + 1 ||
            route.FiniteLimit != SecretCreateReadListWorkload.PageSize ||
            route.MaterializedCandidateCount != SecretCreateReadListWorkload.PageSize)
            throw new PerformanceContractException(
                "Secret native-plan evidence must contain the frozen list-filtered route with physical cardinality 68 and a 16-row bounded page.");
    }

    private static void ValidateRecoveryNativeRoutes(
        PerformanceWorkload workload,
        IReadOnlyList<NativeRouteEvidence> routes)
    {
        if (!string.Equals(workload.Id, RuntimeRecoveryScanWorkload.WorkloadId, StringComparison.Ordinal))
            return;

        var expectedRoutes = RuntimeRecoveryScanWorkload.NativeRouteIdentities;
        var routeNamesMatch = workload.RequiredNativeRoutes.Count == expectedRoutes.Count &&
                              workload.RequiredNativeRoutes.Order(StringComparer.Ordinal)
                                  .SequenceEqual(expectedRoutes.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
                              routes.Count == expectedRoutes.Count &&
                              routes.Select(route => route.RouteIdentity).Distinct(StringComparer.Ordinal).Count() == expectedRoutes.Count &&
                              routes.All(route => expectedRoutes.Contains(route.RouteIdentity, StringComparer.Ordinal));
        var routeFactsMatch = routes.All(route =>
            route.PhysicalCardinality == RuntimeRecoveryScanWorkload.ExecutionCount &&
            route.MaterializedCandidateCount == 1 &&
            route.FiniteLimit == 1);

        if (!routeNamesMatch || !routeFactsMatch)
            throw new PerformanceContractException(
                "Recovery native-plan evidence must contain exactly the four frozen route names and bind physical cardinality 2048, one materialized candidate, and a finite limit of one.");
    }

    private static void ValidateRuntimeScheduleNativeRoutes(
        PerformanceWorkload workload,
        IReadOnlyList<NativeRouteEvidence> routes)
    {
        var expectedRoutes = workload.Id switch
        {
            "due-timer-selection" => new[] { "list-due" },
            "recurring-schedule-selection" => new[] { "list-due", "page-by-publication" },
            _ => null
        };
        if (expectedRoutes is null)
            return;

        if (routes.Count != expectedRoutes.Length ||
            !routes.Select(route => route.RouteIdentity).Order(StringComparer.Ordinal)
                .SequenceEqual(expectedRoutes.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new PerformanceContractException(
                $"{workload.Id} native-plan evidence must contain exactly its frozen bounded route identities.");

        foreach (var route in routes)
        {
            var definition = RuntimeNativePlanContract.For(workload.Id, route.RouteIdentity);
            if (route.PhysicalCardinality != definition.PhysicalCardinality ||
                route.FiniteLimit != definition.FiniteLimit ||
                route.MaterializedCandidateCount != definition.FiniteLimit)
                throw new PerformanceContractException(
                    $"{workload.Id} route '{route.RouteIdentity}' does not bind its frozen physical cardinality, finite limit, and materialized count.");

        }
    }

    private static void ValidateDiagnosticsNativeRoutes(
        PerformanceWorkload workload,
        IReadOnlyList<NativeRouteEvidence> routes)
    {
        if (!string.Equals(workload.Id, DiagnosticsDurableHistoryWorkload.WorkloadId, StringComparison.Ordinal))
            return;

        var expected = DiagnosticsDurableHistoryWorkload.NativeRouteCardinalities;
        var namesMatch = routes.Select(route => route.RouteIdentity).Distinct(StringComparer.Ordinal).Count() == routes.Count &&
                         routes.All(route => expected.ContainsKey(route.RouteIdentity));
        var factsMatch = routes.All(route =>
            expected.TryGetValue(route.RouteIdentity, out var cardinality) &&
            route.PhysicalCardinality == cardinality &&
            route.FiniteLimit == DiagnosticsDurableHistoryWorkload.NativeRouteLimits[route.RouteIdentity] &&
            route.MaterializedCandidateCount == route.FiniteLimit);
        if (!namesMatch || !factsMatch)
            throw new PerformanceContractException(
                "Diagnostics native-plan evidence must contain only declared diagnostics routes and bind their frozen cardinalities and strictly bounded route limits.");

        // Route booleans are an admission summary, not authority. The retained provider artifacts are
        // structurally checked alongside their digest below, after the generic reference and file checks.
        // That prevents a staged JSON envelope from making a table scan look covered merely by setting
        // HasStorageScopePredicate/HasRoutePredicate to true.
    }

    private static void ValidateSecretConcurrency(
        PerformanceWorkload workload,
        RunRequest request,
        SecretProviderConcurrencyEvidence? evidence)
    {
        if (!string.Equals(workload.Id, SecretCreateReadListWorkload.WorkloadId, StringComparison.Ordinal))
        {
            if (evidence is not null)
                throw new PerformanceContractException("Only the Secret workload may publish Secret provider-concurrency evidence.");
            return;
        }

        var expectedSerialized = string.Equals(request.Provider, "sqlite", StringComparison.Ordinal) &&
                                 string.Equals(request.Adapter, "groundwork-secret-repository", StringComparison.Ordinal);
        var expectedConnections = expectedSerialized ? 1 : 2;
        if (evidence is null ||
            evidence.IndependentClientCount != SecretCreateReadListWorkload.ConcurrentContenders ||
            evidence.CompletedContenders != SecretCreateReadListWorkload.ConcurrentContenders ||
            evidence.ProviderCommandStartCount != SecretCreateReadListWorkload.ConcurrentContenders ||
            evidence.ProviderCommandOverlapObserved == expectedSerialized ||
            evidence.ProviderCommandsSerializedByDesign != expectedSerialized ||
            !evidence.EveryContenderIssuedProviderCommands ||
            evidence.DistinctPhysicalConnectionCount != expectedConnections)
            throw new PerformanceContractException(
                "Secret correctness evidence must retain the exact provider-command overlap/serialization, contender-command, and physical-connection proof.");
    }

    public static bool SameMachineEnvironment(MachineMetadata first, MachineMetadata second) =>
        first.OperatingSystem == second.OperatingSystem &&
        first.Runtime == second.Runtime &&
        first.ProcessArchitecture == second.ProcessArchitecture &&
        first.OperatingSystemArchitecture == second.OperatingSystemArchitecture &&
        first.ProcessorCount == second.ProcessorCount &&
        first.HostFingerprintSha256 == second.HostFingerprintSha256;

    public static bool SameTargetTuple(RunRequest first, RunRequest second) =>
        first.ComparisonCohortId == second.ComparisonCohortId &&
        first.MeasurementSetId == second.MeasurementSetId &&
        first.WorkloadId == second.WorkloadId &&
        first.WorkloadVersion == second.WorkloadVersion &&
        first.Provider == second.Provider &&
        first.Adapter == second.Adapter &&
        first.PhysicalForm == second.PhysicalForm &&
        first.Scale == second.Scale &&
        first.CommitSha == second.CommitSha &&
        first.HarnessAssemblySha256 == second.HarnessAssemblySha256 &&
        first.CompositionFingerprint == second.CompositionFingerprint &&
        first.HostFingerprintSha256 == second.HostFingerprintSha256 &&
        first.ProviderVersion == second.ProviderVersion &&
        first.ProviderTopology == second.ProviderTopology &&
        first.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(second.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)) &&
        first.Seed == second.Seed &&
        first.InputFingerprintSha256 == second.InputFingerprintSha256 &&
        first.NativePlanIdentity == second.NativePlanIdentity &&
        first.NativePlanEvidenceReference == second.NativePlanEvidenceReference &&
        first.NativePlanContentSha256 == second.NativePlanContentSha256 &&
        first.PackageVersions.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(second.PackageVersions.OrderBy(pair => pair.Key, StringComparer.Ordinal));

    private static bool ValidMachine(MachineMetadata machine) =>
        !string.IsNullOrWhiteSpace(machine.OperatingSystem) &&
        !string.IsNullOrWhiteSpace(machine.Runtime) &&
        !string.IsNullOrWhiteSpace(machine.ProcessArchitecture) &&
        !string.IsNullOrWhiteSpace(machine.OperatingSystemArchitecture) &&
        machine.ProcessorCount > 0 &&
        IsSha256(machine.HostFingerprintSha256) &&
        DateTimeOffset.TryParse(machine.TimestampUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _);
    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class PerformanceContractException(string message) : Exception(message);
