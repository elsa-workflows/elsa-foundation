namespace Elsa.Integrations.Connectors.Microsoft365;

/// <summary>
/// Bounded retry for Graph 429, 503 and 504. Honours <c>Retry-After</c>, caps each wait and the
/// total wait, and never delays past the activity cancellation token.
/// </summary>
internal sealed class Microsoft365RetryOptions
{
    public static Microsoft365RetryOptions Default { get; } = new();

    /// <summary>Total HTTP attempts, including the first send. Default 3.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Per-wait ceiling when Graph asks for a longer <c>Retry-After</c>. Default 30s.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Ceiling for the sum of waits across retries. Default 60s.</summary>
    public TimeSpan MaxTotalWait { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Wait used when Graph omits <c>Retry-After</c>.</summary>
    public TimeSpan DefaultRetryAfter { get; init; } = TimeSpan.FromSeconds(1);

    public Func<TimeSpan, CancellationToken, Task>? DelayAsync { get; init; }
}

/// <summary>Upload-session chunking. Tests override the chunk size to force multiple chunks.</summary>
internal sealed class Microsoft365UploadOptions
{
    public static Microsoft365UploadOptions Default { get; } = new();

    public int SimpleUploadMaxBytes { get; init; } = Microsoft365Api.SimpleUploadMaxBytes;

    public int ChunkSizeBytes { get; init; } = Microsoft365Api.DefaultUploadChunkSizeBytes;
}
