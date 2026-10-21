namespace Elsa.Maps.Generator;

/// <summary>
/// Dependency-free contract tests for the published map set's link pinning and for the redirect that keeps
/// <c>publish</c> from touching the working tree (spec 257).
/// </summary>
public static class MapSetPublisherContractTests
{
    private const string RepoUrl = "https://github.com/elsa-workflows/elsa-foundation";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    public static void Run()
    {
        var published = new HashSet<string>(StringComparer.Ordinal)
        {
            "docs/maps/test-map.md", "docs/maps/domain-map.md", "docs/maps/dependency-map.json", "docs/reports/maps-v1-findings.md"
        };

        Rewrites("in-set sibling stays relative", "[d](domain-map.md)", "[d](domain-map.md)", published);
        Rewrites("in-set report stays relative", "[f](../reports/maps-v1-findings.md)", "[f](../reports/maps-v1-findings.md)", published);
        Rewrites("source project is pinned",
            "[T](../../Essentials/tests/X/Tests/X.Tests.csproj)",
            $"[T]({RepoUrl}/blob/{Sha}/Essentials/tests/X/Tests/X.Tests.csproj)", published);
        Rewrites("published dataset next to the maps stays relative",
            "[ds](dependency-map.json)", "[ds](dependency-map.json)", published);
        Rewrites("out-of-set report keeps its anchor when pinned",
            "[r](../reports/other.md#top)", $"[r]({RepoUrl}/blob/{Sha}/docs/reports/other.md#top)", published);
        Rewrites("anchor-only link untouched", "[a](#summary)", "[a](#summary)", published);
        Rewrites("absolute URL untouched", "[u](https://example.com/x)", "[u](https://example.com/x)", published);
        Rewrites("link leaving the repository untouched", "[x](../../../outside.md)", "[x](../../../outside.md)", published);
        Rewrites("every link in a row is handled",
            "| [A](../../a/A.csproj) | [d](domain-map.md) |",
            $"| [A]({RepoUrl}/blob/{Sha}/a/A.csproj) | [d](domain-map.md) |", published);

        RedirectResolves();
    }

    private static void Rewrites(string name, string input, string expected, IReadOnlySet<string> published)
    {
        var actual = MapSetPublisher.RewriteLinks(input, "docs/maps/test-map.md", published, RepoUrl, Sha);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"publish link rewrite '{name}': expected '{expected}', got '{actual}'.");
    }

    // publish relies on the redirect for "never writes the working tree"; pin that a repo path maps under the output root
    // and that an unrelated path is left alone.
    private static void RedirectResolves()
    {
        var repoRoot = Path.Join(Path.GetTempPath(), "elsa-publish-contract-repo");
        var outputRoot = Path.Join(Path.GetTempPath(), "elsa-publish-contract-out");
        try
        {
            MarkdownTable.Redirect = (repoRoot, outputRoot);
            var inside = MarkdownTable.Resolve(Path.Combine(repoRoot, "docs", "maps", "test-map.md"));
            if (!string.Equals(inside, Path.Combine(outputRoot, "docs", "maps", "test-map.md"), StringComparison.Ordinal))
                throw new InvalidOperationException($"publish redirect: a repo path must resolve under the output root, got '{inside}'.");

            var outside = Path.Combine(Path.GetTempPath(), "elsewhere.md");
            if (!string.Equals(MarkdownTable.Resolve(outside), outside, StringComparison.Ordinal))
                throw new InvalidOperationException("publish redirect: a path outside the repository must not be redirected.");
        }
        finally
        {
            MarkdownTable.Redirect = null;
        }
    }
}
