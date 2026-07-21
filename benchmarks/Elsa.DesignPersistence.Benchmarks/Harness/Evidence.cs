namespace Elsa.DesignPersistence.Benchmarks.Harness;

/// <summary>Protocol knobs. The real acceptance thresholds (>=100 ops, >=30s steady state) are the
/// defaults; <see cref="Smoke"/> only shrinks them for wiring verification and is stamped into
/// evidence so a smoke run can never be mistaken for an acceptance run.</summary>
public sealed record ProtocolSettings(int MinOperations, double MinSteadyStateSeconds, int WarmupOperations, bool IsSmoke)
{
    public static readonly ProtocolSettings Acceptance = new(100, 30.0, 50, false);
    public static readonly ProtocolSettings Smoke = new(20, 1.5, 5, true);
}

/// <summary>Per-operation measured samples for one process run.</summary>
public sealed record OperationSamples(
    string Operation,
    string Kind,
    int Concurrency,
    bool ScaleBearing,
    int Count,
    double SteadyStateSeconds,
    double ThroughputPerSecond,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    IReadOnlyList<double> RawLatenciesMs);

/// <summary>Full evidence for one independent measured process run of one target at one scale.</summary>
public sealed record ProcessRunResult(
    string Target,
    string Scale,
    int Seed,
    int RunIndex,
    bool Warmup,
    bool Smoke,
    bool CorrectnessChecked,
    bool CorrectnessPassed,
    IReadOnlyDictionary<string, string> CorrectnessHashes,
    EnvironmentMetadata Environment,
    IReadOnlyList<OperationSamples> Operations,
    IReadOnlyList<QueryPlanEvidence> QueryPlans);

public sealed record EnvironmentMetadata(
    string Machine,
    string Os,
    string RuntimeVersion,
    int ProcessorCount,
    string Timestamp)
{
    public static EnvironmentMetadata Capture() => new(
        Environment.MachineName,
        System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        Environment.ProcessorCount,
        DateTimeOffset.UtcNow.ToString("O"));
}
