namespace Elsa.DesignPersistence.Benchmarks.Workload;

/// <summary>
/// A fixed benchmark scale. 1K is the correctness/smoke scale, 100K the acceptance scale (EF-ratio
/// gate), and 1M the scale-bearing form-comparison scale. Sizes are fixed constants so evidence is
/// reproducible; <see cref="Smoke"/> is a deliberately tiny variant for wiring verification only and
/// is never a valid acceptance scale.
/// </summary>
public sealed record WorkloadScale(
    string Name,
    int WorkflowDefinitions,
    int ActivityDefinitions,
    int VersionedDefinitions,
    int VersionedActivities,
    int PayloadBytes)
{
    /// <summary>Correctness scale: hashes must match across every adapter and form here.</summary>
    public static readonly WorkloadScale OneThousand = new("1k", 1_000, 300, 300, 90, 512);

    /// <summary>Acceptance scale: every EF-ratio gate is decided here.</summary>
    public static readonly WorkloadScale HundredThousand = new("100k", 100_000, 30_000, 30_000, 9_000, 512);

    /// <summary>Scale-bearing form-comparison scale.</summary>
    public static readonly WorkloadScale OneMillion = new("1m", 1_000_000, 300_000, 300_000, 90_000, 512);

    /// <summary>Tiny wiring-verification scale. Not an acceptance scale.</summary>
    public static readonly WorkloadScale Smoke = new("smoke", 200, 60, 60, 18, 128);

    public bool IsCorrectnessScale => ReferenceEquals(this, OneThousand) || Name == OneThousand.Name;

    public static WorkloadScale Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "1k" or "1000" => OneThousand,
        "100k" or "100000" => HundredThousand,
        "1m" or "1000000" => OneMillion,
        "smoke" => Smoke,
        _ => throw new ArgumentException($"Unknown scale '{value}'. Use 1k, 100k, 1m, or smoke.")
    };
}
