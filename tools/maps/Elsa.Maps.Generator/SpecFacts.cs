namespace Elsa.Maps.Generator;

/// <summary>Status clues read from one <c>specs/&lt;id&gt;/</c> folder. Textual, not authoritative workflow state.</summary>
public sealed record SpecFacts(
    string Id,
    string Title,
    string Status,
    string PlanVerdict,
    int TasksDone,
    int TasksOpen,
    string Notes);

/// <summary>Reads the spec folders for the spec-status map.</summary>
public static class SpecScanner
{
    private static readonly string[] NoteKeywords = ["superseded", "retained", "deferred", "out of scope", "construct-only"];

    /// <summary>Every spec folder, in ordinal id order.</summary>
    public static IReadOnlyList<SpecFacts> Read(RepoContext repo)
    {
        var specsRoot = Path.Combine(repo.Root, "specs");
        if (!Directory.Exists(specsRoot)) return [];

        return Directory.EnumerateDirectories(specsRoot)
            .Select(directory => Path.GetFileName(directory))
            .Order(StringComparer.Ordinal)
            .Select(id => Read(specsRoot, id))
            .ToArray();
    }

    private static SpecFacts Read(string specsRoot, string id)
    {
        var specFile = Path.Combine(specsRoot, id, "spec.md");
        var planFile = Path.Combine(specsRoot, id, "plan.md");
        var tasksFile = Path.Combine(specsRoot, id, "tasks.md");

        var title = id;
        var status = "unknown";
        var notes = string.Empty;

        if (File.Exists(specFile))
        {
            var text = File.ReadAllText(specFile);
            var lines = text.Split('\n');

            var titleLine = lines.FirstOrDefault(line => line.StartsWith("# Feature Specification:", StringComparison.Ordinal));
            if (titleLine is not null)
            {
                var separator = titleLine.IndexOf(": ", StringComparison.Ordinal);
                if (separator >= 0) title = titleLine[(separator + 2)..].TrimEnd('\r');
            }

            var statusLine = lines.FirstOrDefault(line => line.StartsWith("**Status**:", StringComparison.Ordinal));
            if (statusLine is not null) status = statusLine["**Status**: ".Length..].TrimEnd('\r');

            var lower = text.ToLowerInvariant();
            notes = string.Join(", ", NoteKeywords.Where(keyword => lower.Contains(keyword, StringComparison.Ordinal)));
        }

        var verdict = "-";
        if (File.Exists(planFile))
        {
            const string marker = "Constitution Check verdict:";
            var verdictLine = File.ReadLines(planFile).FirstOrDefault(line => line.Contains(marker, StringComparison.Ordinal));
            if (verdictLine is not null)
                verdict = verdictLine[(verdictLine.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..].TrimStart().TrimEnd('\r');
        }

        var (done, open) = CountTasks(tasksFile);

        return new SpecFacts(id, title, status, verdict, done, open, notes.Length > 0 ? notes : "-");
    }

    private static (int Done, int Open) CountTasks(string tasksFile)
    {
        if (!File.Exists(tasksFile)) return (0, 0);

        var done = 0;
        var open = 0;

        foreach (var line in File.ReadLines(tasksFile))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("- [", StringComparison.Ordinal)) continue;

            // "- [x] T001 ..." counts as done, "- [ ] T001 ..." as open; anything else is not a task line.
            if (trimmed.Length < 6) continue;
            var marker = trimmed[3];
            if (trimmed[4] != ']') continue;

            var rest = trimmed[5..].TrimStart();
            if (rest.Length < 2 || rest[0] != 'T' || !char.IsDigit(rest[1])) continue;

            if (marker is 'x' or 'X') done++;
            else if (marker == ' ') open++;
        }

        return (done, open);
    }
}
