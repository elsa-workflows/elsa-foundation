namespace Elsa.Cli;

/// <summary>Checks one published artifact against its reviewed in-memory files before handoff.</summary>
public static class CompositionHandoffFileVerifier
{
    public static void Verify(string candidateDirectory, IReadOnlyDictionary<string, byte[]> expectedFiles)
    {
        ArgumentNullException.ThrowIfNull(candidateDirectory);
        ArgumentNullException.ThrowIfNull(expectedFiles);

        try
        {
            // The second pass catches a persistent edit to an early file while a later file was read.
            // Deployment still needs its own integrity receipt after this point-in-time local check.
            VerifyPass(candidateDirectory, expectedFiles);
            VerifyPass(candidateDirectory, expectedFiles);
        }
        catch (CliRefusal)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw Changed();
        }
    }

    private static void VerifyPass(string candidateDirectory, IReadOnlyDictionary<string, byte[]> expectedFiles)
    {
        var directory = new DirectoryInfo(candidateDirectory);
        if (!directory.Exists || directory.LinkTarget is not null || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw Changed();

        var entries = Directory.EnumerateFileSystemEntries(directory.FullName).ToArray();
        if (entries.Length != expectedFiles.Count ||
            !entries.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal).SetEquals(expectedFiles.Keys))
            throw Changed();

        foreach (var (name, expected) in expectedFiles)
        {
            if (expected is null || name != Path.GetFileName(name))
                throw Changed();
            var info = new FileInfo(Path.Join(directory.FullName, name));
            if (!info.Exists || info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw Changed();
            var actual = File.ReadAllBytes(info.FullName);
            info.Refresh();
            if (!info.Exists || info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                !actual.AsSpan().SequenceEqual(expected))
                throw Changed();
        }
    }

    private static CliRefusal Changed() =>
        CliRefusal.Resolution("candidate-changed", "The published candidate changed after review; generate a fresh candidate.");
}
