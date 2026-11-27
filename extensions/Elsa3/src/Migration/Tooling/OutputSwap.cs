namespace Elsa3.Migration.Tooling;

/// <summary>
/// Writes a run's output into <c>--out</c> as a whole (spec 1237 T023, data-model "Output folder"): everything goes to a
/// hidden <c>.staging-&lt;guid&gt;</c> folder inside <c>--out</c>, on the same volume, and <c>report.json</c>,
/// <c>report.html</c> and <c>migrated/</c> are moved into place last. No other file in <c>--out</c> is touched. A failed run
/// removes its staging folder and leaves the previous output as it was.
/// </summary>
internal sealed class OutputSwap : IDisposable
{
    public const string ReportJsonName = "report.json";
    public const string ReportHtmlName = "report.html";
    public const string MigratedName = "migrated";
    public const string StagingPrefix = ".staging-";
    private const string PreviousName = ".previous";
    private const string CommittedMarkerName = ".committed";

    /// <summary>A staging folder younger than this may belong to a run still in progress on the same output folder.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    private readonly string _outputFolder;
    private bool _committed;
    private bool _keepStaging;

    private OutputSwap(string outputFolder, string stagingFolder)
    {
        _outputFolder = outputFolder;
        StagingFolder = stagingFolder;
    }

    /// <summary>The staging folder the run writes into.</summary>
    public string StagingFolder { get; }

    /// <summary>Creates <c>--out</c> when it is missing, and a fresh staging folder inside it.</summary>
    public static OutputSwap Begin(string outputFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);

        var output = Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(output);
        SweepStaleStaging(output);
        var staging = Path.Combine(output, StagingPrefix + Guid.NewGuid().ToString("N"));
        var info = Directory.CreateDirectory(staging);
        if (OperatingSystem.IsWindows())
            info.Attributes |= FileAttributes.Hidden;
        return new OutputSwap(output, staging);
    }

    /// <summary>
    /// Replaces <c>report.json</c>, <c>report.html</c> and <c>migrated/</c> in <c>--out</c> with the staged ones. The old
    /// entries are moved aside inside the staging folder first, so a failure while moving the new ones in puts the old ones
    /// back.
    /// </summary>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_committed, this);

        // Moved in with report.json last, and aside with report.json first: a consumer never sees a report.json without
        // the migrated/ and report.html it belongs to.
        var moveInOrder = new[] { MigratedName, ReportHtmlName, ReportJsonName };
        foreach (var name in moveInOrder)
        {
            if (!Exists(Path.Combine(StagingFolder, name)))
                throw new InvalidOperationException($"The staged output has no '{name}'.");
        }

        var previous = Path.Combine(StagingFolder, PreviousName);
        Directory.CreateDirectory(previous);
        var movedAside = new List<string>();
        var movedIn = new List<string>();
        try
        {
            foreach (var name in moveInOrder.Reverse())
            {
                var target = Path.Combine(_outputFolder, name);
                if (Exists(target))
                {
                    Move(target, Path.Combine(previous, name));
                    movedAside.Add(name);
                }
            }

            foreach (var name in moveInOrder)
            {
                Move(Path.Combine(StagingFolder, name), Path.Combine(_outputFolder, name));
                movedIn.Add(name);
            }

            // The old output in .previous is superseded from here on. Mark the folder at once, so that a later run's
            // sweep removes it even if this process stops or cannot delete it.
            TryWriteMarker(Path.Combine(StagingFolder, CommittedMarkerName));
        }
        catch (Exception exception)
        {
            // Every restore step is best effort and the rest still runs. Whatever could not be put back stays in the
            // staging folder, which is then kept, so the previous output is never deleted.
            foreach (var name in movedIn)
                TryDelete(Path.Combine(_outputFolder, name));
            var notRestored = movedAside.Where(name => !TryMove(Path.Combine(previous, name), Path.Combine(_outputFolder, name))).ToArray();
            if (notRestored.Length > 0)
            {
                _keepStaging = true;
                throw new IOException(
                    $"The output could not be replaced, and the previous {string.Join(", ", notRestored)} could not be put back; " +
                    $"they are kept in '{previous}'.", exception);
            }

            throw;
        }

        _committed = true;
        TryDelete(previous);
        TryDelete(StagingFolder);
    }

    /// <summary>
    /// Removes the staging folder; after a failed or abandoned run the previous output stays as it was. A staging folder
    /// that holds previous output a failed rollback could not put back is kept.
    /// </summary>
    public void Dispose()
    {
        if (_committed || (!_keepStaging && !HoldsPreviousOutput(StagingFolder)))
            TryDelete(StagingFolder);
    }

    /// <summary>
    /// Removes staging folders that earlier runs left behind when they were killed, unless one still holds previous output
    /// that was moved aside and never put back: that is the only copy, so it stays for the engineer to recover.
    /// </summary>
    /// <remarks>
    /// Only folders older than <see cref="StaleAfter"/> are swept, so a run never removes the live staging folder of another
    /// run on the same output folder.
    /// </remarks>
    private static void SweepStaleStaging(string outputFolder)
    {
        var cutoff = DateTime.UtcNow - StaleAfter;
        foreach (var stale in Directory.EnumerateDirectories(outputFolder, StagingPrefix + "*"))
        {
            if (NewestWriteUtc(stale) > cutoff)
                continue;
            if (File.Exists(Path.Combine(stale, CommittedMarkerName)) || !HoldsPreviousOutput(stale))
                TryDelete(stale);
        }
    }

    /// <summary>Whether the staging folder still holds moved-aside previous output; when that cannot be read, it is kept.</summary>
    private static bool HoldsPreviousOutput(string stagingFolder)
    {
        try
        {
            var previous = Path.Combine(stagingFolder, PreviousName);
            return Directory.Exists(previous) && Directory.EnumerateFileSystemEntries(previous).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// The newest write time of the folder or anything in it: a folder's own time changes only when its direct entries
    /// do, so a long run writing deep inside migrated/ still counts as live. When it cannot be read, the folder is live.
    /// </summary>
    private static DateTime NewestWriteUtc(string folder)
    {
        try
        {
            var newest = Directory.GetLastWriteTimeUtc(folder);
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
            {
                var written = File.GetLastWriteTimeUtc(entry);
                if (written > newest)
                    newest = written;
            }

            return newest;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTime.MaxValue;
        }
    }

    private static void TryWriteMarker(string path)
    {
        try
        {
            File.WriteAllBytes(path, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryMove(string source, string destination)
    {
        try
        {
            Move(source, destination);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void Move(string source, string destination)
    {
        if (Directory.Exists(source))
            Directory.Move(source, destination);
        else
            File.Move(source, destination);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover staging folder is hidden and ignored by the next run's export reader.
        }
    }
}
