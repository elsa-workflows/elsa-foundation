using System.Text.RegularExpressions;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Relative Markdown links in the extension-point catalogs and the module READMEs must resolve, and the root
/// <c>EXTENSION_POINTS.md</c> must link every source catalog. Moving projects one or two directory levels deeper
/// broke 45 such links at once, most of them the catalogs' back-link to the root index, and nothing noticed: a
/// Markdown link that points nowhere renders as an ordinary link until someone clicks it.
/// </summary>
/// <remarks>
/// A target is checked against the files git tracks, not the disk: macOS resolves <c>extensions/</c> to
/// <c>Extensions/</c> where CI's Linux does not, and an untracked local file would satisfy a link nobody else can
/// follow. <c>tools/maps/check-catalog-links.py</c> applies the same rules for a quick local report.
/// </remarks>
public sealed partial class CatalogLinkGuardTests
{
    /// <summary>A catalog's file name, and also the root index's repository-relative path.</summary>
    private const string Catalog = "EXTENSION_POINTS.md";

    private static readonly HashSet<string> Tracked = TrackedFiles();

    private static readonly HashSet<string> TrackedDirectories = Tracked.SelectMany(Ancestors).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"!?\[(?:[^\[\]]|\[[^\]]*\])*\]\(\s*<?([^)\s>]+)>?(?:\s+[""'(][^)]*)?\)")]
    private static partial Regex InlineLink { get; }

    [GeneratedRegex(@"^ {0,3}\[[^\]]+\]:[ \t]*<?([^\s>]+)>?", RegexOptions.Multiline)]
    private static partial Regex ReferenceDefinition { get; }

    [GeneratedRegex("`+[^`]*`+")]
    private static partial Regex InlineCode { get; }

    [GeneratedRegex(@"^\s*(```|~~~)")]
    private static partial Regex Fence { get; }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex Scheme { get; }

    [Fact]
    public void Every_relative_link_in_a_catalog_or_module_readme_resolves()
    {
        var files = Tracked
            .Where(path => Path.GetFileName(path) == Catalog ||
                           Path.GetFileName(path) == "README.md" && (path.StartsWith("Essentials/", StringComparison.Ordinal) || path.StartsWith("Extensions/", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var links = files.SelectMany(file => RelativeLinks(file).Select(link => (File: file, link.Line, link.Target, Resolved: Resolve(file, link.Target)))).ToArray();
        var broken = links
            .Where(link => link.Resolved is null || !Tracked.Contains(link.Resolved) && !TrackedDirectories.Contains(link.Resolved))
            .Select(link => $"{link.File}:{link.Line}: {link.Target}")
            .ToArray();

        Assert.True(files.Count(file => Path.GetFileName(file) == Catalog) > 50 && links.Length > 250,
            $"Only {files.Length} files and {links.Length} relative links were found, so this guard would check almost nothing.");
        Assert.True(broken.Length == 0, "These relative links resolve to no tracked file or directory:" + Environment.NewLine + string.Join(Environment.NewLine, broken));
    }

    [Fact]
    public void The_root_index_links_every_source_catalog()
    {
        var linked = RelativeLinks(Catalog).Select(link => Resolve(Catalog, link.Target)).ToHashSet(StringComparer.Ordinal);
        var unlinked = Tracked
            .Where(path => Path.GetFileName(path) == Catalog && ModuleRoots.IsProductionPath(path) && !linked.Contains(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(linked.Count > 50, $"The root index links only {linked.Count} files, so this guard would check almost nothing.");
        Assert.True(unlinked.Length == 0, $"The root {Catalog} does not link these source catalogs; add each under its domain family:" + Environment.NewLine + string.Join(Environment.NewLine, unlinked));
    }

    /// <summary>
    /// Every link target in <paramref name="file"/> that is neither external nor a same-file anchor. Fenced blocks and
    /// inline code are blanked first, keeping line numbers, and links are matched across the whole text because a link's
    /// text may wrap onto the next line.
    /// </summary>
    private static IEnumerable<(int Line, string Target)> RelativeLinks(string file)
    {
        var inFence = false;
        var kept = File.ReadAllLines(Path.Join(RepoRoot, file)).Select(line =>
        {
            var fence = Fence.IsMatch(line);
            var keep = fence || inFence ? string.Empty : InlineCode.Replace(line, code => new string(' ', code.Length));
            inFence ^= fence;
            return keep;
        });
        var body = string.Join('\n', kept);

        return InlineLink.Matches(body).Concat(ReferenceDefinition.Matches(body))
            .Select(match => (Line: body.AsSpan(0, match.Groups[1].Index).Count('\n') + 1, Target: match.Groups[1].Value))
            .Where(link => !link.Target.StartsWith('#') && !link.Target.StartsWith("//", StringComparison.Ordinal) && !Scheme.IsMatch(link.Target))
            .ToArray();
    }

    /// <summary>
    /// The repository-relative path <paramref name="target"/> names from <paramref name="file"/>, without its fragment or
    /// query; a leading <c>/</c> starts at the repository root, as GitHub renders it. Null when it climbs out of the repository.
    /// </summary>
    private static string? Resolve(string file, string target)
    {
        var path = target.Split('#', '?')[0];
        if (path.Length == 0) return file;

        var segments = new List<string>(path.StartsWith('/') ? [] : file.Split('/').SkipLast(1));
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count == 0) return null;
                segments.RemoveAt(segments.Count - 1);
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }

    private static IEnumerable<string> Ancestors(string path)
    {
        for (var separator = path.LastIndexOf('/'); separator > 0; separator = path.LastIndexOf('/', separator - 1))
            yield return path[..separator];
    }

    private static HashSet<string> TrackedFiles()
    {
        var (exitCode, output) = ChildProcess.Run("git", ["ls-files", "-z"], RepoRoot);
        Assert.True(exitCode == 0, $"git ls-files failed with exit {exitCode}:\n{output}");
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }
}
