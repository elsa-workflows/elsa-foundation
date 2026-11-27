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

    private readonly string _outputFolder;
    private bool _committed;

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

        var names = new[] { MigratedName, ReportHtmlName, ReportJsonName };
        foreach (var name in names)
        {
            if (!Exists(Path.Combine(StagingFolder, name)))
                throw new InvalidOperationException($"The staged output has no '{name}'.");
        }

        var previous = Path.Combine(StagingFolder, ".previous");
        Directory.CreateDirectory(previous);
        var movedAside = new List<string>();
        var movedIn = new List<string>();
        try
        {
            foreach (var name in names)
            {
                var target = Path.Combine(_outputFolder, name);
                if (Exists(target))
                {
                    Move(target, Path.Combine(previous, name));
                    movedAside.Add(name);
                }
            }

            // report.json last: a consumer that sees the new report sees the new migrated/ and report.html with it.
            foreach (var name in names)
            {
                Move(Path.Combine(StagingFolder, name), Path.Combine(_outputFolder, name));
                movedIn.Add(name);
            }
        }
        catch
        {
            foreach (var name in movedIn)
                TryDelete(Path.Combine(_outputFolder, name));
            foreach (var name in movedAside)
                Move(Path.Combine(previous, name), Path.Combine(_outputFolder, name));
            throw;
        }

        _committed = true;
        TryDelete(StagingFolder);
    }

    /// <summary>Removes the staging folder; after a failed or abandoned run the previous output stays as it was.</summary>
    public void Dispose() => TryDelete(StagingFolder);

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
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover staging folder is hidden and ignored by the next run's export reader.
        }
    }
}
