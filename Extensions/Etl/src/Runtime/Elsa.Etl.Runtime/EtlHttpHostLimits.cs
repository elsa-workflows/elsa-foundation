namespace Elsa.Etl.Runtime;

/// <summary>Host ceilings applied independently of authored finite HTTP pipeline limits.</summary>
/// <remarks>These ceilings do not reserve memory or establish provider support. Live attempts also obey the shared runtime capacity.</remarks>
public sealed record EtlHttpHostLimits
{
    /// <summary>Maximum response bytes for one source page.</summary>
    public long MaxPageBytes { get; init; } = 16L * 1024 * 1024;
    /// <summary>Maximum records in one source page.</summary>
    public int MaxPageRecords { get; init; } = 20_000;
    /// <summary>Maximum accepted JSON nesting depth, at most 64.</summary>
    public int MaxJsonDepth { get; init; } = 64;
    /// <summary>Maximum UTF-8 continuation bytes.</summary>
    public int MaxContinuationBytes { get; init; } = 64 * 1024;
    /// <summary>Maximum finite page count for one source partition.</summary>
    public int MaxPagesPerPartition { get; init; } = 100_000;
    /// <summary>Maximum retained source input bytes for one run.</summary>
    public long MaxRetainedInputBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    /// <summary>Maximum physical attempts for one HTTP operation.</summary>
    public int MaxAttempts { get; init; } = 10;
    /// <summary>Maximum HTTP operation deadline in seconds.</summary>
    public int MaxDeadlineSeconds { get; init; } = 3600;
    /// <summary>Maximum single request duration in seconds.</summary>
    public int MaxRequestTimeoutSeconds { get; init; } = 120;
    /// <summary>Maximum configured retry backoff in seconds.</summary>
    public int MaxDelaySeconds { get; init; } = 600;
}
