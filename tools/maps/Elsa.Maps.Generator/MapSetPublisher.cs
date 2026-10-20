using System.Text;
using System.Text.RegularExpressions;

namespace Elsa.Maps.Generator;

/// <summary>
/// Generates the documentation map set into a directory for publication (spec 257): CI uploads it as a build artifact
/// and commits it to the <c>generated-maps</c> branch.
/// </summary>
/// <remarks>
/// The maps link into the source tree with relative paths (<c>../../Essentials/…</c>). On a branch that holds only the
/// maps those would all 404, so every relative link whose target is outside the published set is pinned to the source
/// commit's blob URL. Links between members of the set stay relative and keep working on the branch. The working tree is
/// never written: generation goes through <see cref="MarkdownTable.Redirect"/>.
/// </remarks>
public static class MapSetPublisher
{
    private static readonly Regex Link = new(@"\]\((?<target>[^)\s]+)\)", RegexOptions.Compiled);

    /// <summary>Generates every documentation layer into <paramref name="outputRoot"/> and returns the repo-relative paths written.</summary>
    public static IReadOnlyList<string> Publish(RepoContext repo, string outputRoot, string sourceCommit, string repoUrl)
    {
        outputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(outputRoot);

        List<string> published;
        try
        {
            MarkdownTable.Redirect = (repo.Root, outputRoot);

            // Same order as "all", minus the committed dependency-map dataset: architecture-reference reads summaries
            // out of the domain and extension-point maps, so those come first.
            var projects = ProjectGraph.Read(repo);
            published =
            [
                .. CoreMapsGenerator.Generate(repo, projects),
                .. DomainMapGenerator.Generate(repo, projects),
                .. ExtensionPointMapGenerator.Generate(repo, projects),
                .. ArchitectureReferenceMapGenerator.Generate(repo, projects),
                .. FeatureDependencyMapGenerator.Generate(repo, projects)
            ];
        }
        finally
        {
            MarkdownTable.Redirect = null;
        }

        var set = published.ToHashSet(StringComparer.Ordinal);
        foreach (var path in published.Where(path => path.EndsWith(".md", StringComparison.Ordinal)))
        {
            var file = Path.Combine(outputRoot, path);
            var rewritten = RewriteLinks(File.ReadAllText(file), path, set, repoUrl, sourceCommit);
            MarkdownTable.Write(file, new StringBuilder(rewritten));
        }

        MarkdownTable.Write(Path.Combine(outputRoot, "README.md"), Readme(published, sourceCommit, repoUrl));
        return published;
    }

    /// <summary>
    /// Pins every relative link in <paramref name="content"/> whose target is outside <paramref name="published"/> to
    /// <c>{repoUrl}/blob/{sourceCommit}/{path}</c>. Anchors, absolute URLs and links into the set are left alone, and so
    /// is a link that resolves outside the repository.
    /// </summary>
    public static string RewriteLinks(string content, string filePath, IReadOnlySet<string> published, string repoUrl, string sourceCommit) =>
        Link.Replace(content, match =>
        {
            var target = match.Groups["target"].Value;
            if (target.StartsWith('#') || target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal))
                return match.Value;

            var anchorAt = target.IndexOf('#');
            var (pathPart, anchor) = anchorAt < 0 ? (target, "") : (target[..anchorAt], target[anchorAt..]);
            if (ResolveRepoPath(filePath, pathPart) is not { } repoPath || published.Contains(repoPath))
                return match.Value;

            return $"]({repoUrl.TrimEnd('/')}/blob/{sourceCommit}/{repoPath}{anchor})";
        });

    /// <summary>Resolves <paramref name="relative"/> against the directory of <paramref name="filePath"/>; null when it leaves the repository.</summary>
    private static string? ResolveRepoPath(string filePath, string relative)
    {
        var segments = new List<string>(filePath.Split('/')[..^1]);
        foreach (var segment in relative.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) return null;
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    private static StringBuilder Readme(IReadOnlyList<string> published, string sourceCommit, string repoUrl)
    {
        var readme = new MapBuilder()
            .Line("# Generated maps")
            .Line()
            .Line($"Generated from [`{sourceCommit}`]({repoUrl.TrimEnd('/')}/commit/{sourceCommit}) of `main`. Links into the source tree are pinned to that commit.")
            .Line()
            .Line("This branch is written only by the `Publish generated maps` job in `.github/workflows/ci.yml`, after every push to `main`. Do not commit to it or open pull requests against it. To regenerate the same set locally from a checkout of `main`:")
            .Line()
            .Line("```bash")
            .Line("dotnet run --project tools/maps/Elsa.Maps.Generator -- all")
            .Line("```")
            .Line()
            .Line("What each map holds, and how to read them: [docs/maps/README.md on main](" + repoUrl.TrimEnd('/') + "/blob/main/docs/maps/README.md).")
            .Line()
            .Line("## Files")
            .Line();

        foreach (var path in published.Order(StringComparer.Ordinal))
            readme.Line($"- [{path}]({path})");

        return readme.Content;
    }
}
