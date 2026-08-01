using System.Text;

namespace Elsa.Maps.Generator;

/// <summary>Formatting shared by every generated map.</summary>
public static class MarkdownTable
{
    /// <summary>
    /// A table cell: blank becomes <c>-</c>, and pipes are escaped so they cannot break the row.
    /// </summary>
    public static string Cell(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>Joins values with <c>&lt;br&gt;</c> for multi-line cells.</summary>
    public static string Lines(IEnumerable<string> values) => string.Join("<br>", values);

    /// <summary>
    /// When set, generated files are written under this root instead of the repository, preserving their
    /// relative paths. The freshness check uses it to generate into a scratch directory so it can compare
    /// outputs without touching the working tree.
    /// </summary>
    public static (string RepoRoot, string OutputRoot)? Redirect { get; set; }

    /// <summary>
    /// Writes a file with LF endings and no BOM, which is what the shell generators produced and what the
    /// committed maps contain.
    /// </summary>
    public static void Write(string path, StringBuilder content)
    {
        if (Redirect is { } redirect && path.StartsWith(redirect.RepoRoot, StringComparison.Ordinal))
            path = Path.Combine(redirect.OutputRoot, Path.GetRelativePath(redirect.RepoRoot, path));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    }
}

/// <summary>A <see cref="StringBuilder"/> that always emits LF, so output is identical on every platform.</summary>
public sealed class MapBuilder
{
    private readonly StringBuilder _builder = new();

    /// <summary>Appends a line terminated by LF.</summary>
    public MapBuilder Line(string text = "")
    {
        _builder.Append(text).Append('\n');
        return this;
    }

    /// <summary>Appends a table row from pre-formatted cells.</summary>
    public MapBuilder Row(params string[] cells) => Line($"| {string.Join(" | ", cells)} |");

    /// <inheritdoc />
    public override string ToString() => _builder.ToString();

    /// <summary>The accumulated content.</summary>
    public StringBuilder Content => _builder;
}
