using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// The scans and helpers every <c>EfSchemaFamily*GuardTests</c> class shares (spec 180): one sweep of the tree per
/// scope, computed once and reused by every concern's test class, rather than once per class.
/// </summary>
internal static class EfSchemaFamilyTestFixtures
{
    /// <summary>Every production source under <c>src/</c> that mentions a schema family, a stamp or an upcaster.</summary>
    public static SchemaFamilyScan Production { get; } = SchemaFamilyScan.Of(
        Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !HasSegment(RepoRoot, file, "Migrations"))
            .Select(file => (Path: Path.GetRelativePath(RepoRoot, file), Text: File.ReadAllText(file)))
            .Where(source => source.Text.Contains("SchemaFamily", StringComparison.Ordinal) ||
                             source.Text.Contains("SchemaVersion", StringComparison.Ordinal) ||
                             source.Text.Contains("EfSchemaUpcaster", StringComparison.Ordinal)));

    /// <summary>Where the frozen upcaster fixtures are recorded, one '&lt;sha-256&gt;  &lt;repo-relative path&gt;' line each.</summary>
    public const string FixtureLock = "tests/essentials/Architecture/Baselines/schema-upcaster-fixtures.sha256";

    /// <summary>Every committed upcaster fixture: a file under a <c>Fixtures/SchemaUpcasters</c> directory of a test tree.</summary>
    public static IReadOnlyList<(string Path, string Text)> UpcasterFixtures { get; } =
        new[] { "tests", "src" }
            .SelectMany(root => Directory.EnumerateDirectories(Path.Join(RepoRoot, root), "SchemaUpcasters", SearchOption.AllDirectories))
            .Where(directory => !IsBuildOutput(directory) && Path.GetFileName(Path.GetDirectoryName(directory)) == "Fixtures")
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Select(file => (Path: Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/'), Text: File.ReadAllText(file)))
            .OrderBy(fixture => fixture.Path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The synthetic family's fixture-pair directory, used by the fixture-detector unit tests.</summary>
    public const string Pair = "tests/Module/Fixtures/SchemaUpcasters/Orders/1-to-2/";

    /// <summary>
    /// Every source that can ship an upcaster or prove one: the production tree, every test tree and the samples, whose
    /// upcaster a test project under tests/ proves (the schema-rollout demo's Notes module).
    /// </summary>
    public static SchemaFamilyScan Proving { get; } = SchemaFamilyScan.Of(
        new[] { "src", "tests", "samples" }
            .SelectMany(root => Directory.EnumerateFiles(Path.Join(RepoRoot, root), "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsBuildOutput(file) && !HasSegment(RepoRoot, file, "Migrations"))
            .Select(file => (Path: Path.GetRelativePath(RepoRoot, file), Text: File.ReadAllText(file)))
            .Where(source => source.Text.Contains("EfSchemaUpcaster", StringComparison.Ordinal)));

    /// <summary>
    /// Every production source a stored row's content can be read or written in: every file of an EF persistence project,
    /// read whether or not it mentions a stamp, and every other source <see cref="Production"/> reads. A test project that
    /// lives under <c>src/</c> is left out, since its tests corrupt rows on purpose.
    /// </summary>
    public static SchemaFamilyScan Persistence { get; } = SchemaFamilyScan.Of(
        Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !HasSegment(RepoRoot, file, "Migrations") && !HasSegment(RepoRoot, file, "tests"))
            .Select(file => (Path: Path.GetRelativePath(RepoRoot, file), Text: File.ReadAllText(file)))
            .Where(source => IsPersistenceSource(source.Path) || MentionsSchema(source.Text)));

    /// <summary>A source of an EF persistence project: one under a directory named EntityFrameworkCore or EntityFramework.</summary>
    public static bool IsPersistenceSource(string path) =>
        HasSegment(RepoRoot, Path.Join(RepoRoot, path), "EntityFrameworkCore") || HasSegment(RepoRoot, Path.Join(RepoRoot, path), "EntityFramework");

    public static bool MentionsSchema(string text) =>
        text.Contains("SchemaFamily", StringComparison.Ordinal) ||
        text.Contains("SchemaVersion", StringComparison.Ordinal) ||
        text.Contains("EfSchemaUpcaster", StringComparison.Ordinal);

    public static void AssertNone(IReadOnlyList<string> violations, string rule) =>
        Assert.True(violations.Count == 0, rule + Environment.NewLine + string.Join(Environment.NewLine, violations));

    public static SchemaFamilyScan Scan(string source) => SchemaFamilyScan.Of([("Fixture.cs", source)]);
}
