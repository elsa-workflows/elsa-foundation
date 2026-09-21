using Elsa.Cli.Worker;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Elsa.Cli;

/// <summary>What <c>script-check</c> found wrong with one file.</summary>
public enum ScriptCheckFault
{
    /// <summary>The bytes differ and the module's migrations did not: a statement changed.</summary>
    Edited,

    /// <summary>The bytes differ because the module gained or lost migrations since the file was generated.</summary>
    Stale,

    /// <summary>The plan names the file and the directory does not have it.</summary>
    Missing,

    /// <summary>The directory has the file and the plan does not name it.</summary>
    Orphan
}

/// <summary>One file-level finding, kept distinct from every other kind so a report never conflates them.</summary>
public sealed record ScriptCheckFinding(string File, ScriptCheckFault Fault, string Description);

/// <summary>The outcome of one check: the headline classification, the findings behind it, and the exit code.</summary>
public sealed record ScriptCheckReport(int ExitCode, string Headline, IReadOnlyList<string> Lines);

/// <summary>
/// Compares a committed artifact against what the host regenerates from that artifact's own plan
/// (FR-044–FR-046).
/// </summary>
/// <remarks>
/// <para>
/// A difference is classified as exactly one kind, because the two kinds call for different work.
/// <em>SQL differs</em> means a statement in a file a DBA may already have applied is not what the model
/// produces — someone must read it. <em>Manifest versions differ, SQL identical</em> means a package moved
/// under an unchanged schema: regenerate and commit, with nothing new to apply. Reporting the second as
/// the first sends a reviewer looking for a schema change that does not exist; reporting the first as the
/// second hides one that does.
/// </para>
/// <para>
/// The regenerated manifest is produced from the committed plan's own host facts, so what can move between
/// the two is what the host pins — not which machine the artifact was generated on.
/// </para>
/// </remarks>
public static partial class ScriptCheck
{
    /// <summary>The manifest fields a routine package upgrade moves without changing a single statement.</summary>
    private static readonly string[] VersionFields = ["efCoreVersion", "engine.version"];

    public static ScriptCheckReport Compare(string directory, string regenerated, MigrationPlan plan)
    {
        var regeneratedPlan = MigrationPlan.Read(regenerated);
        var findings = FileFindings(directory, regenerated, plan, regeneratedPlan).ToArray();
        var manifestDifferences = ManifestDifferences(
            File.ReadAllBytes(Path.Join(directory, MigrationPlan.FileName)),
            File.ReadAllBytes(Path.Join(regenerated, MigrationPlan.FileName)));

        if (findings.Length > 0)
        {
            return new(
                ToolExitCode.NegativeResult,
                "SQL differs",
                [
                    .. findings.Select(finding => $"{finding.File}: {finding.Description}"),
                    "Review the differences, then regenerate with `dotnet elsa persistence script` and commit."
                ]);
        }

        if (manifestDifferences.Count == 0)
            return new(ToolExitCode.Success, "up to date", [$"{plan.Modules.Count} file(s) and {MigrationPlan.FileName} match what this host regenerates."]);

        var versionsOnly = manifestDifferences.All(difference => IsVersionField(difference.Path));
        return new(
            ToolExitCode.NegativeResult,
            versionsOnly ? "manifest versions differ, SQL identical" : "manifest differs, SQL identical",
            [
                .. manifestDifferences.Select(difference => $"{difference.Path}: {difference.Committed} -> {difference.Regenerated}"),
                versionsOnly
                    ? "Every .sql file is byte-identical: regenerate and commit the manifest, nothing new to apply."
                    : "Every .sql file is byte-identical, and the manifest no longer describes this host: regenerate and commit it."
            ]);
    }

    /// <summary>
    /// A field whose movement a package upgrade alone explains. Per-module package versions are matched by
    /// shape rather than listed, because the module set is the artifact's, not this build's.
    /// </summary>
    public static bool IsVersionField(string path) =>
        VersionFields.Contains(path, StringComparer.Ordinal) || ModulePackageVersion().IsMatch(path);

    /// <summary>Every field whose value differs between two manifests, by path, including one present in only one of them.</summary>
    public static IReadOnlyList<(string Path, string Committed, string Regenerated)> ManifestDifferences(byte[] committed, byte[] regenerated)
    {
        using var left = JsonDocument.Parse(committed);
        using var right = JsonDocument.Parse(regenerated);
        var before = Flatten(left.RootElement);
        var after = Flatten(right.RootElement);

        return
        [
            .. before.Keys.Union(after.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(path => (
                    Path: path,
                    Committed: before.GetValueOrDefault(path, "(absent)"),
                    Regenerated: after.GetValueOrDefault(path, "(absent)")))
                .Where(difference => !string.Equals(difference.Committed, difference.Regenerated, StringComparison.Ordinal))
        ];
    }

    private static IEnumerable<ScriptCheckFinding> FileFindings(
        string directory,
        string regenerated,
        MigrationPlan plan,
        MigrationPlan regeneratedPlan)
    {
        foreach (var module in plan.Modules.OrderBy(module => module.File, StringComparer.Ordinal))
        {
            var committedFile = Path.Join(directory, module.File);
            var regeneratedFile = Path.Join(regenerated, module.File);
            if (!File.Exists(committedFile))
            {
                yield return new(module.File, ScriptCheckFault.Missing, $"{MigrationPlan.FileName} names it for '{module.Module}' and it is not in the directory.");
                continue;
            }

            if (!File.Exists(regeneratedFile))
            {
                yield return new(module.File, ScriptCheckFault.Stale, $"'{module.Module}' no longer produces this file.");
                continue;
            }

            if (File.ReadAllBytes(committedFile).AsSpan().SequenceEqual(File.ReadAllBytes(regeneratedFile)))
                continue;

            var current = regeneratedPlan.Modules.FirstOrDefault(entry => string.Equals(entry.Module, module.Module, StringComparison.OrdinalIgnoreCase));
            // Two different failures wear the same byte difference. A module that gained or lost a migration
            // since the artifact was generated is out of date and needs regenerating; a file whose bytes
            // moved while its migration set did not had its statements changed, and someone has to read it.
            yield return current is not null && !current.Ids.SequenceEqual(module.Ids, StringComparer.Ordinal)
                ? new(module.File, ScriptCheckFault.Stale,
                    $"out of date: '{module.Module}' records {module.Ids.Count} migration(s) and now has {current.Ids.Count}.")
                : new(module.File, ScriptCheckFault.Edited,
                    $"differs from what regenerates while '{module.Module}' has the same migrations: a statement changed, review required.");
        }

        var planned = plan.Modules.Select(module => module.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*.sql").Select(Path.GetFileName).Order(StringComparer.Ordinal))
            if (file is not null && !planned.Contains(file))
                yield return new(file, ScriptCheckFault.Orphan, $"present in the directory and {MigrationPlan.FileName} does not name it: stale or orphaned.");
    }

    private static Dictionary<string, string> Flatten(JsonElement element)
    {
        var flat = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(element, "", flat);
        return flat;
    }

    private static void Walk(JsonElement element, string path, Dictionary<string, string> flat)
    {
        switch (element.ValueKind)
        {
            // An empty container is recorded as a value of its own: a manifest that dropped every entry
            // from a list would otherwise differ only by absent keys, which reads as nothing having moved.
            case JsonValueKind.Object when element.EnumerateObject().Any():
                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}", flat);
                break;
            case JsonValueKind.Array when element.EnumerateArray().Any():
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Walk(item, $"{path}[{index++}]", flat);
                break;
            case JsonValueKind.Object:
                flat[path] = "{}";
                break;
            case JsonValueKind.Array:
                flat[path] = "[]";
                break;
            default:
                flat[path] = element.GetRawText();
                break;
        }
    }

    [GeneratedRegex(@"^modules\[\d+\]\.package\.version$")]
    private static partial Regex ModulePackageVersion();
}
