using System.Text.Json;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Gate G4 (RFC #1191 Part 2, spec 150 FR-B1): reports which consumer-facing surface carries no
/// <c>[ConsumerNote]</c>, so an omission is visible and chosen rather than silent.
/// </summary>
/// <remarks>
/// <para>
/// <b>This gate never fails a build, and that is the design, not a shortcut.</b> A gate that demanded a note
/// per enum member would be satisfied within a week by filler text restating the member's name, and filler is
/// worse than absence: it looks like an answer. What the report guarantees is that whoever changes the
/// surface sees the list.
/// </para>
/// <para>
/// It ranks by what the benchmark showed actually costs a consumer. Enum-typed inputs come first: an
/// unexplained enum member is the shape that cost three sessions a failed run, because the member name
/// (<c>async</c>) reads as a scheduling detail rather than as "your response will not arrive".
/// </para>
/// </remarks>
public sealed class ConsumerNoteReport
{
    public int Run(string repoRoot, string configuration)
    {
        var contractsDirectory = Path.Combine(repoRoot, "docs", "contracts", "fragments");
        if (!Directory.Exists(contractsDirectory))
        {
            Console.Error.WriteLine($"'{contractsDirectory}' does not exist; run merge first.");
            return 1;
        }

        var enumInputs = new List<string>();
        var activities = new List<string>();
        var features = new List<string>();

        foreach (var path in Directory.EnumerateFiles(contractsDirectory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            using var fragment = JsonDocument.Parse(File.ReadAllBytes(path));
            var assembly = fragment.RootElement.GetProperty("assembly").GetString()!;

            foreach (var feature in Items(fragment.RootElement, "features"))
            {
                if (!HasNotes(feature))
                    features.Add($"{assembly}: feature '{feature.GetProperty("id").GetString()}'");
            }

            foreach (var activity in Items(fragment.RootElement, "activities"))
            {
                var key = activity.GetProperty("activityTypeKey").GetString()!;
                if (!HasNotes(activity))
                    activities.Add($"{assembly}: {key}");

                foreach (var input in Items(activity, "inputs"))
                {
                    // An enum input with no per-member note is the highest-value gap: the value space is
                    // published (enumValues) but what choosing each value DOES is not.
                    if (input.TryGetProperty("enumValues", out var values) &&
                        values.ValueKind == JsonValueKind.Array &&
                        values.GetArrayLength() > 0 &&
                        !HasNotes(input))
                    {
                        enumInputs.Add($"{assembly}: {key}.{input.GetProperty("name").GetString()} " +
                                       $"({string.Join('|', values.EnumerateArray().Select(value => value.GetString()))})");
                    }
                }
            }
        }

        Report("Enum-typed inputs with no per-member note (finding F1)", enumInputs);
        Report("Activities with no note (finding F2)", activities);
        Report("Features with no note (finding F2)", features);

        Console.WriteLine();
        Console.WriteLine(
            $"G4 (advisory): {enumInputs.Count} enum input(s), {activities.Count} activity(ies) and " +
            $"{features.Count} feature(s) carry no consumer note. This never fails a build — a forced note is " +
            "filler, and filler reads as an answer. Add one where you know something a consumer cannot see.");
        return 0;
    }

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
            : [];

    private static bool HasNotes(JsonElement element) =>
        element.TryGetProperty("notes", out var notes) &&
        notes.ValueKind == JsonValueKind.Array &&
        notes.GetArrayLength() > 0;

    private static void Report(string title, IReadOnlyList<string> entries)
    {
        Console.WriteLine();
        Console.WriteLine($"{title}: {entries.Count}");
        foreach (var entry in entries.Take(20))
            Console.WriteLine($"  - {entry}");
        if (entries.Count > 20)
            Console.WriteLine($"  … and {entries.Count - 20} more.");
    }
}
