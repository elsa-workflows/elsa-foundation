namespace Elsa.Contracts.Generator;

/// <summary>
/// Check mode (spec 149 FR-006 / research R8): regenerates the contract artifacts into a scratch
/// directory and byte-compares every file — <c>manifest.json</c> INCLUDED, unlike the maps check, because
/// per-fragment fingerprints are contract, not bookkeeping — against the committed <c>docs/contracts/</c>.
/// Exit 0 when identical; exit 1 with the stale file list and the regenerate-and-commit remediation.
/// </summary>
public sealed class ContractsFreshness(Diagnostics diagnostics)
{
    public int Run(string repoRoot, string configuration)
    {
        var committedDirectory = Path.Combine(repoRoot, "docs", "contracts");
        var scratchDirectory = Path.Combine(Path.GetTempPath(), $"elsa-contracts-check-{Environment.ProcessId}");
        try
        {
            var mergeExit = new ContractsMerge(diagnostics).Run(repoRoot, configuration, scratchDirectory);
            if (mergeExit != 0)
                return mergeExit;

            var stale = Compare(committedDirectory, scratchDirectory).ToList();
            if (stale.Count == 0)
            {
                Console.WriteLine("Committed contracts match the tree.");
                return 0;
            }

            foreach (var file in stale)
            {
                diagnostics.Error(Path.Combine(committedDirectory, file), "ELSACT010", "Committed contract artifact does not match the tree.");
                PrintFirstDifference(committedDirectory, scratchDirectory, file);
            }
            Console.Error.WriteLine();
            Console.Error.WriteLine("Committed contracts are stale. Refresh them and commit the result:");
            Console.Error.WriteLine($"  dotnet build Elsa.Server.slnx -c {configuration}");
            Console.Error.WriteLine("  dotnet run --project tools/contracts/Elsa.Contracts.Generator -- merge");
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(scratchDirectory))
                    Directory.Delete(scratchDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort scratch cleanup.
            }
        }
    }

    /// <summary>
    /// Shows the first differing line of a stale artifact so a CI failure reveals WHAT drifted, not just
    /// that something did — without this, an environment-dependent generation bug is undiagnosable from logs.
    /// </summary>
    private static void PrintFirstDifference(string committedDirectory, string regeneratedDirectory, string file)
    {
        var committedPath = Path.Combine(committedDirectory, file);
        var regeneratedPath = Path.Combine(regeneratedDirectory, file);
        if (!File.Exists(committedPath) || !File.Exists(regeneratedPath))
        {
            Console.Error.WriteLine($"  {(File.Exists(committedPath) ? "only committed" : "only regenerated")}: {file}");
            return;
        }

        var committedLines = File.ReadAllLines(committedPath);
        var regeneratedLines = File.ReadAllLines(regeneratedPath);
        for (var index = 0; index < Math.Max(committedLines.Length, regeneratedLines.Length); index++)
        {
            var committedLine = index < committedLines.Length ? committedLines[index] : "<end of file>";
            var regeneratedLine = index < regeneratedLines.Length ? regeneratedLines[index] : "<end of file>";
            if (!string.Equals(committedLine, regeneratedLine, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"  first difference at line {index + 1}:");
                Console.Error.WriteLine($"    committed:   {Truncate(committedLine)}");
                Console.Error.WriteLine($"    regenerated: {Truncate(regeneratedLine)}");
                return;
            }
        }

        Console.Error.WriteLine("  files differ only in line endings or trailing bytes.");

        static string Truncate(string line) => line.Length <= 200 ? line : line[..200] + "…";
    }

    /// <summary>Public for direct unit testing (§2.23.2); the CLI path goes through <see cref="Run"/>.</summary>
    public static IEnumerable<string> Compare(string committedDirectory, string regeneratedDirectory)
    {
        var committed = ListFiles(committedDirectory);
        var regenerated = ListFiles(regeneratedDirectory);

        // README.md is authored documentation living beside the generated artifacts; it is not generated.
        var comparableFiles = committed.Keys.Union(regenerated.Keys, StringComparer.Ordinal)
            .Where(file => !string.Equals(file, "README.md", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);
        foreach (var file in comparableFiles)
        {
            if (!committed.TryGetValue(file, out var committedPath) ||
                !regenerated.TryGetValue(file, out var regeneratedPath) ||
                !File.ReadAllBytes(committedPath).AsSpan().SequenceEqual(File.ReadAllBytes(regeneratedPath)))
            {
                yield return file;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> ListFiles(string root)
    {
        if (!Directory.Exists(root))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => path, StringComparer.Ordinal);
    }
}
