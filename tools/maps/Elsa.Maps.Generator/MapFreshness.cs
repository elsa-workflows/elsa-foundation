namespace Elsa.Maps.Generator;

/// <summary>
/// Reports whether the committed maps still describe the tree.
/// </summary>
/// <remarks>
/// <para>
/// The check regenerates every map into a scratch directory and compares the bytes with what is
/// committed. It deliberately does <b>not</b> gate on <c>input_fingerprint</c>: that changes on every
/// source edit, so a fingerprint gate would oblige every code PR to regenerate and commit eleven map
/// files, even though most source edits change no map at all. Comparing outputs asks the question that
/// actually matters — are the committed maps still true? — and stays quiet otherwise.
/// </para>
/// <para>
/// It exists because the committed maps went three projects stale and carried 25 corrupted rows with
/// nothing objecting. Generation itself stays manually initiated; this only reports.
/// </para>
/// </remarks>
public static class MapFreshness
{
    /// <summary>Returns 0 when the committed maps match freshly generated ones, 1 otherwise.</summary>
    public static int Check(RepoContext repo)
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"elsa-maps-check-{Environment.ProcessId}");

        try
        {
            Directory.CreateDirectory(scratch);
            MarkdownTable.Redirect = (repo.Root, scratch);

            var projects = ProjectGraph.Read(repo);
            var generated = new List<string>();
            generated.AddRange(CoreMapsGenerator.Generate(repo));
            generated.AddRange(DomainMapGenerator.Generate(repo, projects));
            generated.AddRange(ExtensionPointMapGenerator.Generate(repo, projects));
            generated.AddRange(ArchitectureReferenceMapGenerator.Generate(repo, projects));
            generated.AddRange(FeatureDependencyMapGenerator.Generate(repo, projects));

            // manifest.json carries input_fingerprint and git metadata that move with every commit, so it
            // is regenerated but not compared — it is bookkeeping, not a description of the tree.
            var stale = generated
                .Where(path => !path.EndsWith("manifest.json", StringComparison.Ordinal))
                .Where(path => !SameBytes(Path.Combine(repo.Root, path), Path.Combine(scratch, path)))
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (stale.Length == 0)
            {
                Console.WriteLine("Generated maps still describe the tree.");
                return 0;
            }

            Console.Error.WriteLine(
                $"""
                 Generated maps no longer describe the tree. {stale.Length} file(s) would change:

                 {string.Join(Environment.NewLine, stale.Select(path => "  " + path))}

                 Refresh them and commit the result:

                   dotnet run --project tools/maps/Elsa.Maps.Generator -- all
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
