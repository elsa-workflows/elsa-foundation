using System.Text.Json;

namespace Elsa.Maps.Generator;

/// <summary>
/// Compares the committed <c>docs/maps/manifest.json</c> fingerprint against the current tree.
/// </summary>
/// <remarks>
/// This exists because the committed maps were three projects stale and carried 25 corrupted rows
/// without anything objecting. Generation stays manually initiated; this only reports that a
/// refresh is due, and writes nothing.
/// </remarks>
public static class MapFreshness
{
    /// <summary>Returns 0 when the maps match the tree, 1 when a refresh is due.</summary>
    public static int Check(RepoContext repo)
    {
        var manifestPath = Path.Combine(repo.Root, "docs", "maps", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine("docs/maps/manifest.json is missing; run the map generator.");
            return 1;
        }

        var (fingerprint, inputs) = CoreMapsGenerator.ComputeFingerprint(repo);

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var committed = document.RootElement.TryGetProperty("input_fingerprint", out var value)
            ? value.GetString()
            : null;

        if (string.Equals(committed, fingerprint, StringComparison.Ordinal))
        {
            Console.WriteLine($"Generated maps are fresh ({inputs.Count} inputs, fingerprint {fingerprint[..12]}\u2026).");
            return 0;
        }

        Console.Error.WriteLine(
            $"""
             Generated maps are stale.

               committed input_fingerprint: {committed ?? "(absent)"}
               current input_fingerprint:   {fingerprint}
               inputs hashed:               {inputs.Count}

             Refresh them and commit the result:

               dotnet run --project tools/maps/Elsa.Maps.Generator -- all
             """);
        return 1;
    }
}
