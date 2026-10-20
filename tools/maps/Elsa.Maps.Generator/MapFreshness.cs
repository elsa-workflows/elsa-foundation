namespace Elsa.Maps.Generator;

/// <summary>
/// Reports whether the committed dependency-map dataset still describes the tree.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>docs/maps/dependency-map.json</c> is committed: the versioning calculator reads it from each commit's
/// tree, so it has to be true at every commit. The documentation maps, their manifest and the findings reports are
/// not committed (spec 257). They are generated locally with <c>-- all</c> and published by CI after each merge, so
/// there is nothing of theirs to hold fresh, and committing them made concurrent PRs conflict.
/// </para>
/// <para>
/// The check regenerates the dataset into a scratch directory and compares the bytes with what is committed. It
/// deliberately does <b>not</b> gate on a fingerprint over the inputs, which would move on every source edit; most
/// source edits change no project-graph fact, and comparing outputs stays quiet for them. The solution filters have
/// their own check, <c>solution-filters-check</c>.
/// </para>
/// </remarks>
public static class MapFreshness
{
    /// <summary>Returns 0 when the committed dataset matches a freshly generated one, 1 otherwise.</summary>
    public static int Check(RepoContext repo)
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"elsa-maps-check-{Environment.ProcessId}");

        try
        {
            Directory.CreateDirectory(scratch);
            MarkdownTable.Redirect = (repo.Root, scratch);

            var stale = DependencyMap.Generate(repo, ProjectGraph.Read(repo))
                .Where(path => !SameBytes(Path.Combine(repo.Root, path), Path.Combine(scratch, path)))
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (stale.Length == 0)
            {
                Console.WriteLine("The committed dependency map still describes the tree.");
                return 0;
            }

            Console.Error.WriteLine(
                $"""
                 The committed dependency map no longer describes the tree. {stale.Length} file(s) would change:

                 {string.Join(Environment.NewLine, stale.Select(path => "  " + path))}

                 Refresh it and commit the result:

                   dotnet run --project tools/maps/Elsa.Maps.Generator -- dependency-map
                 """);
            return 1;
        }
        finally
        {
            MarkdownTable.Redirect = null;
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    private static bool SameBytes(string left, string right) =>
        File.Exists(left) && File.Exists(right) && File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
}
