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
    ProcessKind ProcessKind,
    int ProcessIndex);
public sealed record CorrectnessEvidence(string ObservedResultDigestSha256, string ProviderPrerequisite, IReadOnlyList<string> NativeRoutes, IReadOnlyList<string> EvidenceReferences);
public sealed record OperationSample(string Operation, int Count, double SteadyStateSeconds, double ThroughputPerSecond, double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, IReadOnlyList<double> RawLatenciesMilliseconds);
public sealed record ProcessArtifact(RunRequest Request, BenchmarkProtocol Protocol, bool CorrectnessPassed, CorrectnessEvidence Correctness, IReadOnlyList<OperationSample> Operations, MachineMetadata Machine);
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
    public static async Task<ProcessArtifact> ExecuteAsync(PerformanceWorkload workload, RunRequest request, BenchmarkProtocol protocol, IBenchmarkAdapter adapter, CancellationToken cancellationToken)
    {
        protocol.Validate();
        ArtifactSafety.ValidateRequest(request);
        if (workload.Id != request.WorkloadId || workload.Version != request.WorkloadVersion || workload.Input.Seed != request.Seed || workload.Input.FingerprintSha256 != request.InputFingerprintSha256 || string.IsNullOrWhiteSpace(request.NativePlanIdentity) || string.IsNullOrWhiteSpace(request.NativePlanEvidenceReference) || !workload.RequiredProviders.Contains(request.Provider, StringComparer.Ordinal) || !workload.PhysicalFormsFor646.Contains(request.PhysicalForm, StringComparer.Ordinal))
            throw new PerformanceContractException("The run request does not match the frozen workload/provider/form contract.");
        await adapter.PrepareAsync(cancellationToken);
        var correctness = await adapter.VerifyCorrectnessAsync(cancellationToken);
        ValidateCorrectness(workload, request.Provider, correctness);
        var operations = new List<OperationSample>();
        foreach (var operation in adapter.Operations)
        {
            if (request.ProcessKind == ProcessKind.Warmup)
                await WarmAsync(operation, protocol.WarmupOperations, cancellationToken);
            else
                operations.Add(await MeasureAsync(operation, protocol, cancellationToken));
        }
        return new ProcessArtifact(request, protocol, true, correctness, operations, new MachineMetadata(System.Runtime.InteropServices.RuntimeInformation.OSDescription, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(), Environment.ProcessorCount, DateTimeOffset.UtcNow.ToString("O")));
    }

    private static void ValidateCorrectness(PerformanceWorkload workload, string provider, CorrectnessEvidence evidence)
    {
        if (evidence.ObservedResultDigestSha256 != workload.Correctness.ResultDigestSha256 || string.IsNullOrWhiteSpace(evidence.ProviderPrerequisite) || evidence.ProviderPrerequisite != workload.RequiredProviderEvidence[provider] || evidence.EvidenceReferences.Count == 0 || workload.RequiredNativeRoutes.Except(evidence.NativeRoutes, StringComparer.Ordinal).Any())
            throw new PerformanceContractException("Correctness equality and all required provider/route evidence must pass before timing begins.");
        ArtifactSafety.Validate(evidence);
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

public sealed class PerformanceContractException(string message) : Exception(message);
