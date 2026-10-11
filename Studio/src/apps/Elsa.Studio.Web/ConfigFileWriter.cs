namespace Elsa.Studio.Web;

/// <summary>
/// Shared helpers for mutating the host's on-disk JSON configuration files (shells.json,
/// nuplane-management.json) crash-safely. Writes go to a sibling temp file that is then atomically
/// moved into place, so a process crash mid-write can never leave a truncated or half-written config
/// file behind — readers observe either the old or the new content, never a partial one.
/// </summary>
internal static class ConfigFileWriter
{
    // Process-wide gate guarding every read-modify-write cycle over the host config files. All config
    // mutations (shell feature saves and module-management feed/retention edits) take this before
    // reading, so concurrent requests can't interleave and lose each other's changes.
    public static readonly SemaphoreSlim WriteGate = new(1, 1);

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/> atomically: the payload is first
    /// written to a temp file in the same directory (guaranteeing the same volume, so the move is a
    /// rename rather than a copy) and then moved over the destination with overwrite.
    /// </summary>
    public static async Task WriteAtomicAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, contents, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of the temp file; leaving it behind is harmless.
        }
    }
}
