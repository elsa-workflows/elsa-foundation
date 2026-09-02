namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Serializes the process-global explain environment used by Groundwork.Diagnostics. A capture scope
/// owns both the temporary artifact directory and restoration of the caller's environment, so two
/// provider captures can never redirect one another's native plans.
/// </summary>
internal static class NativeExplainCaptureGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<Scope> EnterAsync(string directoryPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPrefix);
        await Gate.WaitAsync(cancellationToken);

        var previousFlag = Environment.GetEnvironmentVariable("GW_EXPLAIN_ASSERT");
        var previousDirectory = Environment.GetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR");
        var directory = Path.Combine(Path.GetTempPath(), $"{directoryPrefix}-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", "1");
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", directory);
            return new Scope(previousFlag, previousDirectory, directory);
        }
        catch
        {
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", previousFlag);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", previousDirectory);
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Preserve the setup failure; the directory is only temporary diagnostics state.
            }
            Gate.Release();
            throw;
        }
    }

    internal sealed class Scope : IAsyncDisposable
    {
        private readonly string? previousFlag;
        private readonly string? previousDirectory;
        private int disposed;

        internal Scope(string? previousFlag, string? previousDirectory, string directory)
        {
            this.previousFlag = previousFlag;
            this.previousDirectory = previousDirectory;
            Directory = directory;
        }

        public string Directory { get; }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return ValueTask.CompletedTask;

            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", previousFlag);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", previousDirectory);
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // Retained artifacts have already been copied; cleanup must not mask capture failure.
            }
            Gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
