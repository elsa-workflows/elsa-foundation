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
                diagnostics.Error(Path.Combine(committedDirectory, file), "ELSACT010", "Committed contract artifact does not match the tree.");
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

    /// <summary>Public for direct unit testing (§2.23.2); the CLI path goes through <see cref="Run"/>.</summary>
    public static IEnumerable<string> Compare(string committedDirectory, string regeneratedDirectory)
    {
        var committed = ListFiles(committedDirectory);
        var regenerated = ListFiles(regeneratedDirectory);

        foreach (var file in committed.Keys.Union(regenerated.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            // README.md is authored documentation living beside the generated artifacts; it is not generated.
            if (string.Equals(file, "README.md", StringComparison.OrdinalIgnoreCase))
                continue;

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
